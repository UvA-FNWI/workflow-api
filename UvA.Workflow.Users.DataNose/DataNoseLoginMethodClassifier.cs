using System.Text.RegularExpressions;

namespace UvA.Workflow.Users.DataNose;

public class DataNoseLoginMethodClassifier : ILoginMethodClassifier
{
    // DataNose stores Provider on UserAuthentications; workflow users only have a UID, so infer from that.
    public LoginMethod? Classify(string? userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
            return null;

        var uid = userName.Trim();
        const string amcSuffix = "@amc.nl";
        // Some AMC rows store the C-number as C123456@amc.nl.
        if (uid.EndsWith(amcSuffix, StringComparison.OrdinalIgnoreCase))
            uid = uid[..^amcSuffix.Length];

        // C + 6 digits: AMC (C123456).
        if (Regex.IsMatch(uid, @"^C\d{6}$", RegexOptions.IgnoreCase))
            return LoginMethod.Amc;
        // 3 letters + 3 digits: VU VUnetID (abc123).
        if (Regex.IsMatch(uid, @"^[A-Za-z]{3}\d{3}$"))
            return LoginMethod.Vu;
        // 7 digits: UvA student number.
        if (Regex.IsMatch(uid, @"^\d{7}$"))
            return LoginMethod.Uva;
        // 8 alphanumeric chars starting with a letter: UvA employee (UvAnetID).
        // Skip id... (legacy/import noise) and anything with @ so emails are not treated as employees.
        if (uid.Length == 8
            && char.IsLetter(uid[0])
            && uid.All(char.IsLetterOrDigit)
            && !uid.StartsWith("id", StringComparison.OrdinalIgnoreCase))
            return LoginMethod.Uva;
        // UUID: Amsterdam UMC.
        if (Guid.TryParse(uid, out _))
            return LoginMethod.AmsterdamUmc;
        return null;
    }
}