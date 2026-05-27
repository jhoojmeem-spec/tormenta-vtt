using Godot;
using TormentaVTT.Models;

namespace TormentaVTT.Services
{
    public static class PersistenceService
    {
        public static bool SaveCampaign(Campaign campaign, string path)
        {
            var data = campaign.ToDictionary();
            var json = Json.Stringify(data, "\t", false, false);

            var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.WriteRead);
            if (file == null)
                return false;

            file.StoreString(json);
            file.Close();
            return true;
        }

        public static Campaign? LoadCampaign(string path)
        {
            if (!Godot.FileAccess.FileExists(path))
                return null;

            var contents = Godot.FileAccess.GetFileAsString(path);
            var parse = Json.ParseString(contents);
            if (parse.VariantType != Variant.Type.Dictionary)
                return null;

            var data = parse.AsGodotDictionary();
            return Campaign.FromDictionary(data);
        }
    }
}
