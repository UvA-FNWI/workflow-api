namespace UvA.Workflow.Entities.Domain;

public class BilingualString
{
    public string En { get; set; } = "";
    public string Nl { get; set; } = "";

    public BilingualString()
    {
    }

    public BilingualString(string en, string nl)
    {
        En = en;
        Nl = nl;
    }

    public static implicit operator BilingualString(string text) => new() { En = text, Nl = text };

    public static BilingualString operator +(BilingualString s1, BilingualString? s2)
        => s2 == null ? s1 : new BilingualString(s1.En + s2.En, s1.Nl + s2.Nl);
}