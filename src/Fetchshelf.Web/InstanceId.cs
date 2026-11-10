using System.Security.Cryptography;

namespace Fetchshelf.Web;

/// <summary>
/// A random id for this installation, made once and kept in the data folder next to the keys that encrypt its cookies.
/// Browsers don't tell cookies apart by port, so two installations on one host (say, a second container on another
/// port) would otherwise share cookie names and each overwrite the other's antiforgery cookie, which then fails every
/// form with HTTP 400. Naming the cookies after this id keeps them apart.
/// </summary>
public static class InstanceId
{
    public const string FileName = "instance-id";

    public static string LoadOrCreate(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, FileName);
        if (File.Exists(path))
        {
            var stored = File.ReadAllText(path).Trim();
            if (IsValid(stored))
            {
                return stored;
            }
        }

        var id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));
        File.WriteAllText(path, id + Environment.NewLine);
        return id;
    }

    private static bool IsValid(string id) => id.Length is > 0 and <= 32 && id.All(char.IsAsciiLetterOrDigit);
}
