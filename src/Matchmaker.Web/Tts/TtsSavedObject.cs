using System.Text.Json;

namespace Matchmaker.Web.Tts;

/// <summary>
/// L'objet « Warhammer Matchmaker » au format « Saved Object » de Tabletop Simulator : un bloc aplati
/// qui porte le script de tts/WarhammerMatchmaker.lua.
/// </summary>
public static class TtsSavedObject
{
    public const string ObjectName = "Warhammer Matchmaker";

    private static readonly Lazy<byte[]> Content = new(() => JsonSerializer.SerializeToUtf8Bytes(new
    {
        SaveName = ObjectName,
        Date = "",
        VersionNumber = "",
        GameMode = "",
        Note = "",
        LuaScript = "",
        LuaScriptState = "",
        XmlUI = "",
        ObjectStates = new[]
        {
            new
            {
                GUID = "a40k01",
                Name = "BlockSquare",
                Transform = new { posX = 0, posY = 1, posZ = 0, rotX = 0, rotY = 180, rotZ = 0, scaleX = 8, scaleY = 0.2, scaleZ = 5 },
                Nickname = ObjectName,
                Description = "Envoie le score de la feuille LCT à warhammer-matchmaker.com",
                ColorDiffuse = new { r = 0.12, g = 0.12, b = 0.16 },
                Locked = false,
                Grid = true,
                Snap = true,
                Autoraise = true,
                Sticky = true,
                Tooltip = true,
                LuaScript = Script(),
                LuaScriptState = "",
                XmlUI = "",
            },
        },
    }, new JsonSerializerOptions { WriteIndented = true }));

    public static byte[] Json() => Content.Value;

    public static string Script()
    {
        using var stream = typeof(TtsSavedObject).Assembly.GetManifestResourceStream("WarhammerMatchmaker.lua")
            ?? throw new InvalidOperationException("Script de l'objet TTS absent de l'assemblage.");
        return new StreamReader(stream).ReadToEnd();
    }
}
