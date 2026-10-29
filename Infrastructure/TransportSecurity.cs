using System.Net.Security;
using System.Security.Authentication;
using Coflnet.Core.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Coflnet.Sky.Crafts.Infrastructure;

internal static class TransportSecurity
{
    public static void ConfigureTransportSecurity(this IWebHostBuilder builder) =>
        builder.ConfigureKestrel((context, options) =>
        {
            options.ListenAnyIP(8000);
            var section = context.Configuration.GetSection("HTTPS:TLS");
            if (!section.Exists()) return;
            var owner = options.ApplicationServices.GetRequiredKeyedService<PemTlsOptions>("HTTPS");
            options.ListenAnyIP(8443, listen => listen.UseHttps(new TlsHandshakeCallbackOptions
            {
                OnConnection = _ => new(CreateServerAuthenticationOptions(owner,
                    section.GetValue<bool>("REQUIRE_CLIENT_CERTIFICATE")))
            }));
        });

    internal static SslServerAuthenticationOptions CreateServerAuthenticationOptions(
        PemTlsOptions owner, bool requireClientCertificate = false) => new()
    {
        ServerCertificateContext = owner.GetCertificateContext(),
        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
        ClientCertificateRequired = true,
        RemoteCertificateValidationCallback = (sender, certificate, chain, errors) =>
            certificate is null ? !requireClientCertificate :
                owner.ValidateClientCertificate(sender, certificate, chain, errors)
    };
}
