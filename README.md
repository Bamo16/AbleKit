# AbletonSampleData

A .NET library that reads what Ableton Live 12 knows about your samples:

- **the analysis file** (`.asd`) beside each sample: its warp markers, warp mode, time signature,
  default clip and waveform overview;
- **the tags** in each folder's `Ableton Folder Info` XMP store, which it can also write;
- **the tags** in Live's own file index, the database behind the browser.

> [!CAUTION]
> **Unofficial.** Not made, endorsed or supported by Ableton. The formats are undocumented and were
> worked out from real files, so a Live update may break this library. Use at your own risk, and
> back up your library before writing tags.

The `.asd` layout is written up in full in
[**The Ableton Live 12 analysis file**](docs/analysis-file-format.md), which, as far as I know, is
the only public description of the Live 12 format.

## Install

```
dotnet add package AbletonSampleData --prerelease
```

.NET 10. Versions below 1.0 may change the API between releases.

## Read a warp

```csharp
using AbletonSampleData;

if (AnalysisFile.TryRead(@"C:\Samples\Break.wav.asd", out var analysis))
{
    // Null until Save Default Clip is pressed, which is also when Live writes the markers.
    if (analysis.DefaultClip is { } clip)
        Console.WriteLine($"clip: beat {clip.Start} to {clip.End}");

    foreach (var marker in analysis.Markers)
        Console.WriteLine($"{marker.Seconds:F3} s is beat {marker.Beat}");

    // Live stores no tempo, only markers; a tempo is the slope between two of them.
    Console.WriteLine($"{analysis.TempoAt(0):F2} BPM at the start");
}
```

`TryRead` returns false for a file it cannot read or does not recognise, and never throws for a
bad file. `TryParse` does the same for bytes already in memory.

## Read tags

```csharp
var folder = @"C:\Samples\Drums";

// The XMP store in the folder: the files directly in it, with the keywords you have hidden.
var fromXmp = new FolderInfoReader().Read(folder);

// Live's index: every file under the folder, at any depth. Read-only.
var fromIndex = new FileIndexReader(FileIndexReader.DefaultFolder).Read(folder);

if (fromIndex.TryGet(@"C:\Samples\Drums\Kick.wav", out var tags))
    Console.WriteLine(string.Join(", ", tags.Keywords)); // Drums|Kick, Key|C♯, …
```

Keywords are raw, as Live writes them: `Category|Value`, with sharps as `♯` (U+266F). When the two
sources disagree, the index is what Live's browser shows; the XMP can lag behind it, sometimes
indefinitely.

## Write tags

```csharp
var outcome = new FolderInfoWriter().Apply(
    folder,
    [new TagAssignment { RelativePath = "Kick.wav", Keywords = ["Drums|Kick", "Key|C♯", "Key|Minor"] }]
);

if (outcome is { IsSuccess: false, IsStale: true })
    Console.WriteLine("The store changed while writing; nothing was written. Try again.");
```

What the writer does:

- **It replaces a file's keywords** with the ones given. It does not add to them. Read the entry
  first and merge if you want to keep what is there.
- **It leaves every other entry alone**, in its place, and never touches hidden keywords.
- **It writes all or nothing.** A keyword that is not `Category|Value`, or a key spelt in a way Live
  does not use (`Key|C#`, `Key|Db`), rejects the whole write. Live would otherwise create a second,
  permanent tag.
- **It refuses to overwrite a store Live changed** after it was read, and writes through a
  temporary file swapped into place. That narrows the race with Live; it cannot close it.
- **It writes bytes Live's way**: no BOM, LF line endings, three-space indents, and a new store
  under the file name Live itself uses.
- `Rename` moves an entry to a new file name, keeping its keywords.

It keeps no backup and has no dry run. For bulk tagging from a command line, with a dry run by
default and optional backups, see [LiveTagger](https://github.com/17cupsofcoffee/LiveTagger).

## Compared with other projects

| | this | [LiveTagger](https://github.com/17cupsofcoffee/LiveTagger) | [AbletonParsing](https://github.com/DBraun/AbletonParsing) | [ableton-asd-parser](https://github.com/Verbalize-public/ableton-asd-parser) |
|---|---|---|---|---|
| Language | C# library | Rust command line | Python | Python |
| Live 12 `.asd` | reads | | Live 9 and 10 | reads |
| Warp mode, default clip, saved flag | yes | | | |
| XMP tags | reads, writes | reads, writes | | |
| Hidden keywords | reads | | | |
| Live's file index | reads | | | |
| Licence | MIT | MIT | MIT | none |

## Tested on

Windows, Live 12.3 to 12.4, against one library of about 950 samples. Tag stores are per folder and
paths use Windows separators; macOS is untested. Reports from other setups are welcome.

## Maintenance

Built for my own use. Issues are welcome; fixes are best-effort. A pull request with a fixture that
reproduces the problem is the fastest route.

## Licence

[MIT](LICENSE). The test fixture `sidecar-loop-on.asd` is from
[DBraun/AbletonParsing](https://github.com/DBraun/AbletonParsing), MIT, with its licence beside it.
