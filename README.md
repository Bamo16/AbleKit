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

## Read an analysis file

```csharp
using AbleKit.Analysis;

if (AnalysisFile.TryRead(@"C:\Samples\Break.wav.asd", out var analysis))
{
    // Null until Save Default Clip is pressed, which is also when Live writes the markers.
    var clip = analysis.DefaultClip;

    if (clip != null)
        Console.WriteLine($"clip: beat {clip.Start} to {clip.End}");

    foreach (var marker in analysis.Warp.Markers)
        Console.WriteLine($"{marker.Seconds:F3} s is beat {marker.Beat}");

    // The warp holds markers, not a tempo: between two markers, the tempo is beats over seconds.
    // With fewer than two markers there is none, and TempoAt returns null.
    var tempo = analysis.Warp.TempoAt(0);

    if (tempo != null)
        Console.WriteLine($"{tempo:F2} BPM at the start");
}
```

An `AnalysisFile` holds everything in the file, in three parts named after what Live shows:

- **`Clip`**: the clip's start and end, its loop, gain, transpose and detune, HiQ, Fade, colour and
  launch settings;
- **`Warp`**: the warp markers, the warp mode and every mode's settings, and the time signature;
- **`Audio`**: what Live measured in the audio: the waveform overview, the transients, and the file's
  size.

`TryRead` returns false when there is no file. Live writes one only once a sample has been opened,
so for many samples there is none. A file that is there but cannot be read throws:

| exception | means |
|---|---|
| `IOException` | another program has the file locked; trying again may help |
| `UnauthorizedAccessException` | reading the file is not permitted |
| `InvalidDataException` | the file is not one this library reads, cut short or laid out differently; the message says where reading gave up |

`AnalysisFile.Read` does the same, but throws a `FileNotFoundException` when there is no file.
`Parse` and `TryParse` read bytes already in memory.

## Change and write one

Change anything with `with`, then write the result to any path:

```csharp
var clip = analysis.Clip with { PitchCoarse = analysis.Clip.PitchCoarse + 1 };
var raised = analysis with { Clip = clip };

raised.Write(@"C:\Samples\Break (up 1).wav.asd");
```

- **Live pairs an analysis file with the audio file named the same**, minus `.asd`. Where you write
  it, and why, is up to you.
- **Writing a file back gives the bytes Live wrote**, checked against about a thousand real files.
  The one difference: list entries carry internal ids that record a file's editing history, and the
  writer numbers them afresh, as Live does for a new warp.
- **It refuses what Live could not have written**, such as markers out of order or an overview of the
  wrong size, with an `InvalidOperationException`, rather than leave Live to make sense of it.
- **It writes through a temporary file moved into place**, so Live never reads half a file. A file
  it cannot write throws an `IOException`, or an `UnauthorizedAccessException` where writing is not
  permitted, and nothing is written.
- `ToBytes()` gives the bytes without writing them.

## Write one for audio Live has not analysed

Live only analyses a sample once you open it, and only warps it once you save its default clip.
`AudioAnalysis.FromSamples` builds what Live would have measured, from samples you decode yourself
(ffmpeg's `-f f32le`, NAudio, …): the waveform overview, drawn bit for bit as Live draws it, and the
transients you give it. The library does not detect transients.

```csharp
var audio = AudioAnalysis.FromSamples(
    samples,
    channelCount: 2,
    transients: [new Transient(Position: 11025, Energy: 0.8f), /* … */],
    fileSize: new FileInfo(@"C:\Samples\Song (Vocal).flac").Length
);

AnalysisFile stem;

if (AnalysisFile.TryRead(@"C:\Samples\Song (Instrumental).flac.asd", out var sibling))
{
    // Warped like another stem of the same song that you have warped and saved…
    stem = sibling with { Audio = audio };
}
else
{
    // …or from nothing: Live's default clip and warp, set as you like.
    stem = new AnalysisFile { Audio = audio };
}

stem.Write(@"C:\Samples\Song (Vocal).flac.asd");
```

Replacing `Audio` drops everything the sibling measured in its own audio, its transient edits
included, and keeps its clip and warp. Live accepts these files and leaves them as written, as far as
it has been tried: with the full analysis of a sibling stem, with a stand-in for the part of the
analysis nobody understands, and with no transients at all.

See [Writing one](docs/analysis-file-format.md#writing-one) for what was tested.

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

A folder with no store, or one Live's index does not cover, reads as empty. A store or index that
cannot be read throws an `IOException`, or an `UnauthorizedAccessException` where reading is not
permitted. One Live was writing at that moment throws a
`TagStoreChangedException`, which is an `IOException`; reading again fixes it.

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
var kick = new TagAssignment
{
    RelativePath = "Kick.wav",
    Keywords = ["Drums|Kick", "Key|C♯", "Key|Minor"],
};

try
{
    var applied = new FolderInfoWriter().Apply(folder, [kick]);
    Console.WriteLine($"Files tagged: {applied}");
}
catch (TagStoreChangedException)
{
    Console.WriteLine("Live changed the store while it was being written; nothing was written. Try again.");
}
```

What the writer does:

- **It replaces a file's keywords** with the ones given. It does not add to them. Read the entry
  first and merge if you want to keep what is there.
- **It leaves every other entry alone**, in its place, and never touches hidden keywords.
- **It writes all or nothing.** A keyword that is not `Category|Value` throws an `ArgumentException`
  and nothing is written; `FolderInfoWriter.IsWellFormed` checks one first. Any well-formed keyword
  is written, including one Live does not know yet.
- **It refuses to overwrite a store Live changed** after it was read, throwing a
  `TagStoreChangedException`, and writes through a temporary file swapped into place. That narrows the race with Live; it cannot close it.
- **It writes bytes Live's way**: no BOM, LF line endings, three-space indents, and a new store
  under the file name Live itself uses.
- `Rename` moves entries to new file names, keeping their keywords, and returns how many it moved.

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
| Every clip and warp setting | reads, writes | | | | |
| XMP tags | reads, writes | | reads, writes | | |
| Hidden keywords | reads | | | | |
| Live's file index | reads | | | | |
| Licence | MIT | MIT | MIT | MIT | none |

## Tested on

Windows, Live 12.3 to 12.4, against one library of about 950 samples. Tag stores are per folder and
paths use Windows separators; macOS is untested. Reports from other setups are welcome.

## Running the tests

`dotnet test --solution AbleKit.slnx` runs everything that needs no Ableton library: the formats
against committed fixtures, which Live wrote. The rest checks a real library and skips unless two
environment variables point at one:

| variable | holds |
|---|---|
| `ABLEKIT_SAMPLE_FOLDERS` | folders Live has analysed and tagged, separated by `;` |
| `ABLEKIT_PROJECTS_FOLDER` | a folder of Live sets; they are only read, and relinked as copies |

## Maintenance

Built for my own use. Issues are welcome; fixes are best-effort. A pull request with a fixture that
reproduces the problem is the fastest route.

## Licence

[MIT](LICENSE). The test fixture `sidecar-loop-on.asd` is from
[DBraun/AbletonParsing](https://github.com/DBraun/AbletonParsing), MIT, with its licence beside it.
