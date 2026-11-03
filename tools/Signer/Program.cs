using KioskShared;

// Used by the GitHub build:
//   Signer check <signing-public-key.txt>    the SIGNING_KEY secret must match the public key file
//   Signer sign <file> [<file> ...]          writes <file>.sig next to each file
var key = Environment.GetEnvironmentVariable("SIGNING_KEY")?.Trim() ?? "";
if (key == "") { Console.Error.WriteLine("The SIGNING_KEY secret is missing. See UPDATES.md."); return 1; }

try
{
    if (args.Length >= 2 && args[0] == "check")
    {
        if (!File.Exists(args[1])) { Console.Error.WriteLine("signing-public-key.txt is missing in the repository. See UPDATES.md."); return 1; }
        var expected = File.ReadAllText(args[1]).Trim();
        if (Signing.PublicOf(key) != expected) { Console.Error.WriteLine("signing-public-key.txt does not match the SIGNING_KEY secret."); return 1; }
        Console.WriteLine("Signing key OK.");
        return 0;
    }
    if (args.Length >= 2 && args[0] == "sign")
    {
        foreach (var f in args.Skip(1))
        {
            File.WriteAllText(f + ".sig", Signing.Sign(f, key));
            Console.WriteLine("Signed " + f);
        }
        return 0;
    }
}
catch (Exception ex) { Console.Error.WriteLine("Signing failed: " + ex.Message); return 1; }

Console.Error.WriteLine("Usage: Signer check <public key file> | Signer sign <file>...");
return 1;
