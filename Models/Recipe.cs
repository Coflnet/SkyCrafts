using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Coflnet.Sky.Crafts.Models;

public interface IGetIngredients
{
    IEnumerable<string> GetIngredients();
}

public class Recipe : IGetIngredients
{
    [JsonPropertyName("A1")]
    public string A1 { get; set; }

    [JsonPropertyName("A2")]
    public string A2 { get; set; }

    [JsonPropertyName("A3")]
    public string A3 { get; set; }

    [JsonPropertyName("B1")]
    public string B1 { get; set; }

    [JsonPropertyName("B2")]
    public string B2 { get; set; }

    [JsonPropertyName("B3")]
    public string B3 { get; set; }

    [JsonPropertyName("C1")]
    public string C1 { get; set; }

    [JsonPropertyName("C2")]
    public string C2 { get; set; }

    [JsonPropertyName("C3")]
    public string C3 { get; set; }

    [JsonPropertyName("count")]
    public float count { get; set; }

    public virtual IEnumerable<string> GetIngredients()
    {
        yield return A1;
        yield return A2;
        yield return A3;
        yield return B1;
        yield return B2;
        yield return B3;
        yield return C1;
        yield return C2;
        yield return C3;
    }
}
