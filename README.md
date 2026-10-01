# AbleKit

**Read and write Ableton Live's files from .NET.** AbleKit is a C# library for what Ableton Live 12
keeps about the files in your library:

- **the analysis file** (`.asd`) beside each sample: its warp markers, warp mode, time signature,
  default clip, transients and waveform overview, which it can also write;
- **the tags** in each folder's `Ableton Folder Info` XMP store, which it can also write;
- **the tags** in Live's own file index, the database behind the browser, and every tag Live
  knows;
- **the samples your Live sets use**: which sets use a file, and pointing them at it when it moves.

AbleKit is about the files in your library. It does not model what is inside a set, such as tracks,
clips or devices; for that, see [AbleSharp](https://github.com/theokyr/AbleSharp).

> [!CAUTION]
> **Unofficial.** Not made, endorsed or supported by Ableton. The formats are undocumented and were
> worked out from real files, so a Live update may break this library. Use at your own risk, and
> back up your library before writing tags or analysis files.

The `.asd` layout is written up in full in
[**The Ableton Live 12 analysis file**](docs/analysis-file-format.md), which, as far as I know, is
the only public description of the Live 12 format.

## Install

```
dotnet add package AbleKit --prerelease
```

.NET 10. Versions below 1.0 may change the API between releases. Analysis files are in the
`AbleKit.Analysis` namespace, tags in `AbleKit.Tags`, sets in `AbleKit.Sets`.

## Read a warp

```csharp
using AbleKit.Analysis;

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

## Write an analysis file

Live only analyses a sample once you open it, and only writes a warp when you press *Save Default
Clip*. To have a new stem arrive already warped, write its analysis file from a **sibling**: another
stem of the same song that you have warped and saved.

```csharp
// The new stem, decoded to floats from -1 to 1 with its channels interleaved,
// by whatever decoder you use (ffmpeg's -f f32le, NAudio, …).
float[] samples = Decode(@"C:\Samples\Song (Vocal).flac");

var outcome = new AnalysisFileWriter().WriteFromSibling(
    siblingPath: @"C:\Samples\Song (Instrumental).flac.asd",
    audioPath: @"C:\Samples\Song (Vocal).flac",
    samples,
    channelCount: 2,
    transients: [new Transient(Position: 11025, Energy: 0.8f), /* … */]
);

switch (outcome)
{
    case AnalysisWriteOutcome.Written(var path):
        Console.WriteLine($"Wrote {path}");
        break;
    case AnalysisWriteOutcome.Rejected(var error):
        Console.WriteLine($"That sibling will not do: {error}");
        break;
    case AnalysisWriteOutcome.Failed(var error):
        Console.WriteLine($"Could not read or write a file: {error}");
        break;
}
```

What the writer does:

- **It copies the sibling's warp**: markers, default clip, warp mode and every clip setting.
- **It draws the waveform from your samples**, exactly as Live would: it reproduces Live's own
  overview bit for bit.
- **It writes the transients you give it.** It does not detect them. Live shows them as ticks in
  the clip view and warps from them; with none, Live shows none and does not look for its own.
- **It refuses a sibling that does not fit**: one never saved, or one of a different length or
  channel count. Stems of one song are usually the same length, but not always.
- **It replaces any analysis file beside the audio**, writing through a temporary file moved into
  place. The sibling can be the audio's own old file, to keep a warp after the audio was re-cut.

Live accepts these files as its own and leaves them as written, as far as it has been tried. See
[Writing one](docs/analysis-file-format.md#writing-one) for what was tested.

## Read tags

```csharp
using AbleKit.Tags;

var folder = @"C:\Samples\Drums";

// The XMP store in the folder: the files directly in it, with the keywords you have hidden.
var fromXmp = new FolderInfoReader().Read(folder);

// Live's index: every file under the folder, at any depth. Read-only.
var fromIndex = new FileIndexReader(FileIndexReader.DefaultFolder).Read(folder);

if (fromIndex.TryGet(@"C:\Samples\Drums\Kick.wav", out var tags))
    Console.WriteLine(string.Join(", ", tags.Keywords)); // Drums|Kick, Key|C♯, …
```

Keywords are raw, as Live writes them: `Category|Value`, or deeper for Live's nested tags
(`Drums|Cymbal|Crash`). When the two sources disagree, the index is what Live's browser shows; the
XMP can lag behind it, sometimes indefinitely.

## Write tags

Live treats any spelling it has not seen as a new tag: `Key|C#` is not Live's `Key|C♯`, and stays in
the browser until removed by hand. Check keywords against what Live knows before writing them:

```csharp
var known = new FileIndexReader(FileIndexReader.DefaultFolder).ReadKnownKeywords();
var newTags = keywords.Where(keyword => !known.Contains(keyword)).ToList();
// Ask before writing these. KeyTags.Tonics and KeyTags.Modes hold Live's own key spellings.
```

`ReadKnownKeywords` knows every keyword on a file Live has indexed, built-in and your own. A tag you
made but put on no file is missing from it.

```csharp
var outcome = new FolderInfoWriter().Apply(
    folder,
    [new TagAssignment { RelativePath = "Kick.wav", Keywords = ["Drums|Kick", "Key|C♯", "Key|Minor"] }]
);

switch (outcome)
{
    case TagWriteOutcome.Written(var applied):
        Console.WriteLine($"Tagged {applied} files.");
        break;
    case TagWriteOutcome.Stale:
        Console.WriteLine("The store changed while writing; nothing was written. Try again.");
        break;
    case TagWriteOutcome.Rejected(var error):
        Console.WriteLine($"Nothing was written: {error}");
        break;
}
```

What the writer does:

- **It replaces a file's keywords** with the ones given. It does not add to them. Read the entry
  first and merge if you want to keep what is there.
- **It leaves every other entry alone**, in its place, and never touches hidden keywords.
- **It writes all or nothing.** A keyword that is not `Category|Value` rejects the whole write.
  Any well-formed keyword is written, including one Live does not know yet.
- **It refuses to overwrite a store Live changed** after it was read, and writes through a
  temporary file swapped into place. That narrows the race with Live; it cannot close it.
- **It writes bytes Live's way**: no BOM, LF line endings, three-space indents, and a new store
  under the file name Live itself uses.
- `Rename` moves an entry to a new file name, keeping its keywords.

It keeps no backup and has no dry run. For bulk tagging from a command line, with a dry run by
default and optional backups, see [LiveTagger](https://github.com/17cupsofcoffee/LiveTagger).

## Follow a sample into your sets

Renaming or moving a sample leaves every set that uses it showing the file as missing. `LiveSets`
finds those sets and points them at the new path:

```csharp
using AbleKit.Sets;

var sets = new LiveSets(@"C:\Music\Ableton\Projects");

// Which sets use this file, by full path. Live's own backups are left out.
var users = sets.Using([@"C:\Samples\Break.wav"]);

// After moving the file: rewrite each set that used it, keeping a backup first.
var result = sets.Relink(
    [new SampleMove(@"C:\Samples\Break.wav", @"C:\Samples\Drums\Break.wav")],
    backupLabel: "MyTool"
);
```

What the relink does:

- **It changes only the sample's paths**: the file name in the relative path and the whole full path,
  in every reference to it. Nothing else in the set changes.
- **It copies each set to its project's `Backup` folder first**, as
  `Song [MyTool 2026-10-01 093000].als`, beside Live's own backups.
- **It writes the new set beside the old one and swaps it in**, so a failure leaves the set as it was.
  A set it could not rewrite is listed in `result.Failed`, to relink by hand.
- **Close the set in Live first.** Live does not lock a set it has open, and writes its own copy back
  on the next save, undoing the relink.

## Compared with other projects

| | AbleKit | [AbleSharp](https://github.com/theokyr/AbleSharp) | [LiveTagger](https://github.com/17cupsofcoffee/LiveTagger) | [AbletonParsing](https://github.com/DBraun/AbletonParsing) | [ableton-asd-parser](https://github.com/Verbalize-public/ableton-asd-parser) |
|---|---|---|---|---|---|
| Language | C# library | C# library | Rust command line | Python | Python |
| Live sets (`.als`) | finds and relinks samples | reads, writes | | | |
| Live 12 `.asd` | reads, writes | | | Live 9 and 10 | reads |
| Warp mode, default clip, saved flag | yes | | | | |
| XMP tags | reads, writes | | reads, writes | | |
| Hidden keywords | reads | | | | |
| Live's file index | reads | | | | |
| Licence | MIT | MIT | MIT | MIT | none |

## Tested on

Windows, Live 12.3 to 12.4, against one library of about 950 samples. Tag stores are per folder and
paths use Windows separators; macOS is untested. Reports from other setups are welcome.

## Maintenance

Built for my own use. Issues are welcome; fixes are best-effort. A pull request with a fixture that
reproduces the problem is the fastest route.

## Licence

[MIT](LICENSE). The test fixture `sidecar-loop-on.asd` is from
[DBraun/AbletonParsing](https://github.com/DBraun/AbletonParsing), MIT, with its licence beside it.
