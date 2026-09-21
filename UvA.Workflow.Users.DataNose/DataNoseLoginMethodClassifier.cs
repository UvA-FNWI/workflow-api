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
        // 7 digits: UvA student number.
        if (Regex.IsMatch(uid, @"^\d{7}$"))
            return LoginMethod.Uva;
        // 8 alphanumeric chars starting with a letter: UvA employee (UvAnetID).
        // Anything with @ is excluded by the alphanumeric check, so emails are not treated as employees.
        if (uid.Length == 8
            && char.IsLetter(uid[0])
            && uid.All(char.IsLetterOrDigit))
            return LoginMethod.Uva;
        // UUID: Amsterdam UMC.
        if (Guid.TryParse(uid, out _))
            return LoginMethod.AmsterdamUmc;
        return null;
    }
}