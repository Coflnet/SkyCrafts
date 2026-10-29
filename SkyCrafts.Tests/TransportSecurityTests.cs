using Coflnet.Sky.Crafts.Infrastructure;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Coflnet.Core.Security;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Xunit;

namespace Coflnet.Sky.Crafts.Tests;

public class TransportSecurityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeHttpsEnforcesConfiguredClientPolicyAndRenewsCertificate(bool requireClientCertificate)
    {
        var directory = Path.Combine(Path.GetTempPath(), "crafts-tls-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            using var root = CreateRoot();
            using var otherRoot = CreateRoot();
            using var server = Issue(root, server: true);
            using var client = Issue(root, server: false);
            using var untrusted = Issue(otherRoot, server: false);
            File.WriteAllText(Path.Combine(directory, "ca.pem"), root.ExportCertificatePem());
            WriteCertificate(directory, "server", server);
            WriteCertificate(directory, "client", client);
            using var serverOwner = new PemTlsOptions(Path.Combine(directory, "ca.pem"),
                Path.Combine(directory, "server.pem"), Path.Combine(directory, "server.key"), serverCertificate: true);
            using var clientOwner = new PemTlsOptions(Path.Combine(directory, "ca.pem"),
                Path.Combine(directory, "client.pem"), Path.Combine(directory, "client.key"));
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Listen(IPAddress.Loopback, 0);
                options.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(new TlsHandshakeCallbackOptions
                {
                    OnConnection = _ => new(requireClientCertificate
                        ? TransportSecurity.CreateServerAuthenticationOptions(serverOwner, true)
                        : TransportSecurity.CreateServerAuthenticationOptions(serverOwner))
                }));
            });
            await using var app = builder.Build();
            app.MapGet("/probe", (HttpContext context) => context.Connection.ClientCertificate?.Thumbprint ?? "anonymous");
            await app.StartAsync();
            var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;
            var https = addresses.Single(a => a.StartsWith("https:"));
            using (var http = new HttpClient())
                Assert.Equal("anonymous", await http.GetStringAsync(addresses.Single(a => a.StartsWith("http:")) + "/probe"));
            string? observedServer = null;
            HttpClient Connect(X509Certificate2? identity = null)
            {
                var handler = new HttpClientHandler();
                handler.ServerCertificateCustomValidationCallback = (_, certificate, chain, errors) =>
                {
                    observedServer = certificate?.Thumbprint;
                    return clientOwner.ValidateServerCertificate(this, certificate, chain, errors);
                };
                if (identity is not null) handler.ClientCertificates.Add(identity);
                return new HttpClient(handler) { BaseAddress = new Uri(https), Timeout = TimeSpan.FromSeconds(10) };
            }
            using (var anonymous = Connect())
            {
                if (requireClientCertificate)
                    await Assert.ThrowsAsync<HttpRequestException>(async () => await anonymous.GetAsync("/probe"));
                else
                    Assert.Equal("anonymous", await anonymous.GetStringAsync("/probe"));
            }
            using (var trusted = Connect(client))
                Assert.Equal(client.Thumbprint, await trusted.GetStringAsync("/probe"));
            using (var rejected = Connect(untrusted))
                await Assert.ThrowsAsync<HttpRequestException>(async () => await rejected.GetAsync("/probe"));
            using var renewed = Issue(root, server: true);
            WriteCertificate(directory, "server", renewed);
            using (var fresh = Connect(requireClientCertificate ? client : null))
                Assert.Equal(requireClientCertificate ? client.Thumbprint : "anonymous", await fresh.GetStringAsync("/probe"));
            Assert.Equal(renewed.Thumbprint, observedServer);
            await app.StopAsync();
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void WriteCertificate(string directory, string name, X509Certificate2 certificate)
    {
        File.WriteAllText(Path.Combine(directory, name + ".pem"), certificate.ExportCertificatePem());
        using var key = certificate.GetRSAPrivateKey()!;
        File.WriteAllText(Path.Combine(directory, name + ".key"), key.ExportPkcs8PrivateKeyPem());
    }

    private static X509Certificate2 CreateRoot()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=test-root-" + Guid.NewGuid(), key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(3));
    }

    private static X509Certificate2 Issue(X509Certificate2 root, bool server)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=test-leaf", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection
            { new(server ? "1.3.6.1.5.5.7.3.1" : "1.3.6.1.5.5.7.3.2") }, true));
        var san = new SubjectAlternativeNameBuilder();
        san.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(san.Build());
        using var certificate = request.Create(root, DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(1), RandomNumberGenerator.GetBytes(16));
        return certificate.CopyWithPrivateKey(key);
    }
}
