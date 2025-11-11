using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Diagnostics;

string GetBranchName()
{
    var process = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = "rev-parse --abbrev-ref HEAD",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        }
    };
    process.Start();
    string output = process.StandardOutput.ReadToEnd().Trim();
    process.WaitForExit();
    return output;
}

// Function to generate the commit prefix based on the branch name
string CommitPrefix()
{
    string branchName = GetBranchName();
    // Pattern matches:
    // - feat/DN-3174 -> DN-3174
    // - DN-3174 -> DN-3174  
    // - DN-3174-some-description -> DN-3174
    // - feat/DN-3174-some-description -> DN-3174
    var match = Regex.Match(branchName, @"([a-z]+\/([a-z]+-\d+)(?:-.+)?)|([a-z]+-\d+)(?:-.+)?", RegexOptions.IgnoreCase);
    
    if (match.Success)
    {
        // Get the last non-empty group that contains the ticket number
        for (int i = match.Groups.Count - 1; i >= 0; i--)
        {
            var group = match.Groups[i];
            if (group.Success && !string.IsNullOrEmpty(group.Value) && group.Value.Contains('-'))
            {
                return group.Value.ToUpperInvariant();
            }
        }
    }
    return string.Empty;
}

// Function to add the branch key to the commit message
void AddBranchKey(string commitFile)
{
    try
    {
        string prefix = CommitPrefix();
        if (string.IsNullOrEmpty(prefix))
        {
            Console.WriteLine("No ticket prefix found in branch name, skipping prefixing");
            return;
        }

        string content = File.ReadAllText(commitFile);
        if (content.ToUpper().Contains(prefix))
        {
            Console.WriteLine($"Commit message already contains prefix '{prefix}', skipping");
            return;
        }

        File.WriteAllText(commitFile, $"{prefix} {content}");
        Console.WriteLine($"Added prefix '{prefix}' to commit message");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error reading/writing commit file: {ex.Message}");
        Environment.Exit(1);
    }
}

// Main execution
if (Args.Count < 1)
{
    Console.Error.WriteLine("Please check the COMMIT_EDITMSG path and commit message");
    Environment.Exit(1);
}

string commitFile = Args[0];
AddBranchKey(commitFile);
