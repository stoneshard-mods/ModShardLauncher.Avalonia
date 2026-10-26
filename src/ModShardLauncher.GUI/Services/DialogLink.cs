namespace ModShardLauncher.GUI.Services;

/// <summary>A labelled, clickable URL a dialog renders as a link.</summary>
/// <param name="Label">Already localized description shown above the address.</param>
/// <param name="Url">Address opened in the default browser.</param>
public sealed record DialogLink(string Label, string Url);
