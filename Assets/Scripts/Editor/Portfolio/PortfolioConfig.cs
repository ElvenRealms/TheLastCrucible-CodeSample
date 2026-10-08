using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// Recording settings and watched events for <see cref="PortfolioRecorder"/>, read from Tools/Portfolio/portfolio.json.
/// </summary>
public sealed class PortfolioConfig
{
    public const string RelativePath = "Tools/Portfolio/portfolio.json";

    public int width = 1920;
    public int height = 1080;
    public float frameRate = 60f;
    public string outputDir = "Recordings";
    public List<PortfolioEvent> events = new List<PortfolioEvent>();

    /// <summary>
    /// Reads the config. Returns null and sets the error when the file is missing or invalid.
    /// </summary>
    public static PortfolioConfig Load(out string error)
    {
        var path = Path.Combine(Path.GetDirectoryName(Application.dataPath), RelativePath);
        if (!File.Exists(path))
        {
            error = $"{RelativePath} not found";
            return null;
        }
        try
        {
            var config = JsonConvert.DeserializeObject<PortfolioConfig>(File.ReadAllText(path));
            error = config == null || config.width <= 0 || config.height <= 0 || config.frameRate <= 0f
                ? $"{RelativePath} needs a positive width, height and frameRate"
                : null;
            return error == null ? config : null;
        }
        catch (Exception e)
        {
            error = $"{RelativePath} could not be read: {e.Message}";
            return null;
        }
    }
}

/// <summary>
/// An event to log during a take. The label is the event's name in events.jsonl and in the highlight rules.
/// </summary>
public sealed class PortfolioEvent : EventSpec
{
    public string label;
}
