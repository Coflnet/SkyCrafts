using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Coflnet.Sky.Crafts.Models;
using Newtonsoft.Json;

namespace Coflnet.Sky.Crafts.Services;
public interface IReforgeService
{
    Task<Dictionary<string, Reforge>> GetReforges();
}

public class ReforgeService : IReforgeService
{
    public async Task<Dictionary<string, Reforge>> GetReforges()
    {
        var data = await File.ReadAllTextAsync($"itemData/constants/reforgestones.json");
        return JsonConvert.DeserializeObject<Dictionary<string,Reforge>>(data);
    }
}
