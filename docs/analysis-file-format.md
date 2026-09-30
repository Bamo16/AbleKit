# The Ableton Live 12 analysis file (`.asd`)

Live writes an analysis file beside every sample it analyses: `Kick.wav` gets `Kick.wav.asd`.
It holds the waveform overview Live draws, the transients it detected, and, once you press
**Save Default Clip**, how the sample is warped: the warp markers, the warp mode, and where the
clip starts and ends.

Ableton has never documented the format. This page describes what is known about the Live 12
version, how it was worked out, and how confident each part is. It is the reference behind the
[AbletonSampleData](../README.md) reader, but nothing here depends on that library.

**Unofficial.** None of this comes from Ableton, and Ableton owes it no stability. A Live update
can change any of it.

## How sure is this

The layout was read out of real files rather than guessed. Every claim below is tagged:

- **measured**: established on real files, with the count;
- **observed**: seen in Live's UI while editing a sample and comparing the file before and after;
- **guessed**: a reading of a field's name, or a pattern with no test behind it.

The files measured:

| set | files | Live |
|---|---|---|
| The author's sample library (stems of commercial tracks, analysed on Windows) | 947 | 12.3 to 12.4 |
| [DBraun/AbletonParsing](https://github.com/DBraun/AbletonParsing)'s Live 12 test pair, identical except for the clip's Loop switch | 2 | 12 |

All 947 library files parse to their last byte with the rules on this page (measured, 2026-09-30,
with two independent readers). Nothing has been checked on macOS or on Live 11 and earlier.

## The short version

```
head       06 49 · a table of sample positions · 20 fixed bytes · the "default clip saved" byte
chunk      ab 1e 56 78 · 05 · int32 · "SampleData" · a type schema · one SampleData instance
chunk      ab 1e 56 78 · 05 · int32 · an AufTaktData schema and instance (a copy)
```

The useful part is the first chunk, and it describes itself: before the data comes a schema
listing every class in the file with its fields, their names and their types. A reader walks the
schema to find each field, so it needs no hard-coded offsets. The offset of the warp markers is
the same in every file measured, but a reader that used that constant would break on the first
Live update that adds a field ahead of them.

Byte order is **little-endian**, except the lengths of class names, which are big-endian.

## The head

| bytes | contents |
|---|---|
| 2 | `06 49` in every file (measured, 947 of 947) |
| 4 | int32 *n* |
| 4 × *n* | *n* int32 values rising from 0 to the audio's total sample count (measured). What they mark is **not known**: not the transients, and not a FLAC seek table, since a WAV's file has one too |
| 20 | five int32s, `0, 0, 100, 4, 0` in every file (measured, 947 of 947) |
| 1 | **the default clip saved byte**: 0 until *Save Default Clip* is pressed, 1 after (observed). It is 1 on exactly the 936 files with warp markers and 0 on the other 11 (measured) |

The saved byte is the cheapest way to tell whether a sample has been warped and saved: it sits at
offset `6 + 4n + 20`, and no schema is needed to reach it. A careful reader checks that the chunk
magic follows it.

## Chunks

Each chunk starts with 9 bytes: the magic `ab 1e 56 78`, the byte `05`, and an int32: 365 in the
first chunk and 0 in the second, in all 947 files (measured). Nothing in the file is an offset into the
file: no int32 anywhere equals the file length, the schema's offset or a section's size
(measured), so editing one part never means repairing pointers elsewhere.

The first chunk continues with the root class's name, `SampleData`, written as a class name (see
below): `00 0a 53 61 6d 70 6c 65 44 61 74 61`.

The second chunk holds a schema and instance of `AufTaktData`, identical to the `AufTaktData`
inside `SampleData` in all 845 files compared (measured). Readers can ignore it.

## The schema

After `SampleData` comes an int32 count of types, then each type:

| part | encoding |
|---|---|
| name | class name: big-endian u16 length, then ASCII |
| field count | int32. **Negative for a variable-length type** (see below) |
| each field | int32 character count, the name in UTF-16LE, a one-byte type code, and for code `00` a one-byte length and the ASCII class name |

The type codes:

| code | type | size in the instance |
|---|---|---|
| `00` | a class, named after the code | its own fields, inline |
| `10` | bool | 1 byte |
| `11` | int32 | 4 |
| `12` | float32 | 4 |
| `17` | float64 | 8 |
| `31` | byte array | int32 count, then the bytes |
| `32` | 16-bit array (float16 in practice) | int32 count, then 2 × count bytes |
| `35` | int32 array | int32 count, then 4 × count bytes |
| `40` | float32 array | int32 count, then 4 × count bytes |

No other code occurs in the 947 files.

Two classes are declared with a negative field count and have their own encodings in the
instance:

- **`RemoteableList`** and any `List<T>` (field count −1): an int32 **next id**, then for each
  element its class name, an int32 id, and its fields; then an **empty class name, `00 00`**,
  ending the list. The list is never count-prefixed (measured). The int32 before it looks like a
  count and is not one: it is an id allocator, higher than every element's id.
- **`RemoteableArray`** (field count −3): an int32 count, the element class name (written even
  when the count is 0), then the elements packed, with no names or ids.

A Live 12 `SampleData` has **36 fields** in every file measured. Scalar settings are wrapped in
small classes (`RemoteableDouble`, `RemoteableBool`, `UserFloat`, `RemoteableEnum`), each with a
single `Value` field, so the path to the clip's start is `LoopStart.Value`.

## The instance

The instance is the schema's fields in order, with no names, tags or padding. A class-typed field
is its own fields inline; a value is its bytes; an array or list is encoded as above. Walking it
means following the schema field by field and noting where each value lands.

## The fields

In schema order. The spread is over the 845 library files that existed when the fields were
first surveyed (2026-09-26).

### The clip

Units of **beats** are positions on Live's grid, counted from the sample's beat 0.

| field | type | meaning |
|---|---|---|
| `LoopStart` | float64, beats | with Loop off, the clip's **start marker**; with Loop on, the loop start (measured on the DBraun pair) |
| `LoopEnd` | float64, beats | with Loop off, the **end marker**; with Loop on, the loop end (measured) |
| `SampleOffset` | float64, beats | with Loop on, start marker = `LoopStart` + `SampleOffset` (measured on the DBraun pair; 0 in all 845 library files) |
| `HiddenLoopStart`, `HiddenLoopEnd` | float64, beats | the loop brace, remembered while Loop is off (measured on the DBraun pair) |
| `OutMarker` | float64, beats | the end marker in both Loop states on the DBraun pair. In the library it disagrees with `LoopEnd` on 244 of 845 files, all Loop off. Read `LoopEnd` for the clip's end |
| `Sync` | bool | guessed: follows the set's tempo. True everywhere |
| `HiQ` | bool | guessed: high-quality interpolation |
| `Fade` | bool | guessed: clip edge fades |
| `IsWarped` | bool | the clip's Warp switch. **Says nothing on its own**: it is true on files nobody has warped (observed) |
| `SampleVolume` | float32, linear | clip gain (measured as whole decibels on the four files where it is not 1.0) |
| `VelocityAmount`, `PitchCoarse`, `PitchFine` | float32 | guessed by name; 0 everywhere |
| `ColorIndex` | int32 | clip colour; −1 before the first save (observed) |
| `LaunchMode`, `LaunchQuantisation` | int32 | guessed by name |
| `LoopOn` | bool | the Loop switch (measured: the only field that differs within the DBraun pair) |

### The warp

| field | type | meaning |
|---|---|---|
| `WarpMode` | int32 | 0 Beats, 1 Tones, 2 Texture, 3 Re-Pitch, 4 Complex, 5 REX, 6 Complex Pro. **Only 4 and 6 have been seen** (946 and 1 files); the other names follow Live's menu order and are guessed |
| `TransientResolution` … `ComplexProEnvelope` | int32, float32 | the warp modes' parameters; one value each across the library, guessed to be Live's defaults |
| `TimeSignature` | numerator float32, denominator float32, `Time` float64 | 4/4 at 0 everywhere |
| `WarpMarkers` | list of `WarpMarker` | see below |
| `MarkersGenerated` | bool | guessed: the markers came from Live's auto-warp. True on 3 files, each with 2 markers |

### Transients

| field | type | meaning |
|---|---|---|
| `OnSets.Positions` | int32 array, samples | **the transients Live detected**. Different per file, even for stems of one song (measured) |
| `OnSets.TransitionEnergies` | float32 array | one strength per transient, at most 1.0 |
| `UserOnsets` | array of `OnsetEvent` (`Time` seconds, `Energy`, `IsVolatile`) | the transients as the saved clip holds them; empty until *Save Default Clip* (observed) |

**A volatile onset is the transient Live adds under a warp marker** (measured): 15,469 of the
library's 15,474 volatile onsets sit exactly on a marker's time, all with energy `double.MaxValue`.

### Tempo analysis

`AufTaktData` ("Auftakt" is German for upbeat) holds `PreprocessedDataChunk` (bytes),
`UnbiasedTempoEstimate` (float64 BPM, `double.MaxValue` when unset), `IsSet` and `Version`. It is
set on only 14 of 845 files, and on those it matched the warped tempo within 1 BPM on 9. Nothing
found says what makes Live fill it in. Do not treat it as a tempo source.

### The audio and the overview

| field | type | meaning |
|---|---|---|
| `ExtraLength` | int32 | 0 everywhere |
| `OriginalFileSize` | int32, bytes | the audio's size when it was analysed (measured: matches on 828 of 845). Live does not re-analyse a file whose audio later changed length |
| `OverView.OverViewLevels` | list of float16 arrays | **the waveform**: a minimum and a maximum per channel per bin, interleaved, as float16. Checked against the decoded audio to within 0.0005 at every level (measured) |
| `OverView.SamplesPerBinLog2` | int32 | level *k*'s bin is 2^(`SamplesPerBinLog2` × (*k* + 1)) samples. 7 up to 379 s of audio, 8 from 387 s |
| `OverView.ChannelCount`, `.Version` | int32 | 2 and 2 everywhere |

The overview is most of the file: in a 509 KB file, the finest overview level is 455 KB, the
head's table 30 KB and the transients 18 KB. The clip settings and the markers are under 300
bytes. The median file measured is 694 KB.

## Warp markers

A `WarpMarker` has two float64 fields, **`SecTime`** (a position in the audio, in seconds from its
start) and **`BeatTime`** (the grid position it is pinned to). That is all.

- **No tempo is stored anywhere.** A tempo is the slope between two consecutive markers:
  `(beat₂ − beat₁) / (sec₂ − sec₁) × 60` BPM.
- **Seconds are absolute into the audio.** A marker list copied into the file of another,
  sample-aligned file warps it identically (observed).
- **Live adds a hidden marker 1/32 beat after the last one.** A warp with one marker placed by
  hand, the commonest kind, is stored as two, and the hidden one carries the tempo past the last
  visible marker. 926 of the 936 warped files end in one (measured). Live's UI does not show it.
- **Nothing is written until *Save Default Clip*.** Pressing Warp and warping a sample fully left
  the file byte for byte unchanged; the save wrote the markers, the clip, the colour and the saved
  byte, and rewrote the files of every sample warped with it as a group (observed). A file without
  markers is therefore either unwarped or a warp in progress, and the two cannot be told apart on
  disk.

In bytes, one marker in a list is 32 bytes: the class name `00 0a` + `WarpMarker` (12), the int32
id, then the two float64s. From the DBraun Live 12 file, the second of its two markers:

```
00 0a 57 61 72 70 4d 61 72 6b 65 72    class name, "WarpMarker"
02 00 00 00                            id 2
5f c5 0c 3a 7c 7a 90 3f                SecTime  0.016092244…
00 00 00 00 00 00 a0 3f                BeatTime 0.03125 (1/32)
```

With the first marker at 0 s and beat 0, this is the hidden handle alone: a warp at
0.03125 / 0.016092… × 60 = **116.52 BPM**.

## Writing one

Nothing on this page has been used to generate a file from scratch. What is known about how Live
treats a file it did not write, from editing existing files (observed, 2026-09-15 and 2026-09-30):

- **Live applies markers it did not write.** A file whose markers were patched to a different
  tempo played at the patched tempo.
- **The default clip saved byte is required.** The same patched file with that byte set to 0 was
  ignored entirely: the sample came in unwarped.
- **Live does not check that the file belongs to its audio.** Another song's analysis file,
  renamed to sit beside a different sample of a different length, was applied without complaint.
  A writer has to guarantee the right file itself.
- **Live did not rewrite any of the edited files** on loading them.
- **A marker list can be replaced in place.** Swapping one file's marker run and its id allocator
  for another's was enough; there are no offsets to fix.

## What is not known

- What the head's table of sample positions marks.
- The meaning of the fixed fields guessed above, and the warp mode numbers other than 4 and 6.
- Whether `IsWarped` is ever false in practice.
- Whether a file with no overview or no transients is accepted.
- Anything about Live 11 and earlier. DBraun/AbletonParsing reads Live 9 and 10, whose files are
  laid out differently.

Corrections are welcome, best with a file that shows the difference.

## Credits

- [DBraun/AbletonParsing](https://github.com/DBraun/AbletonParsing) (MIT) for its descriptions of
  the clip fields and its Live 12 test pair, one of which is this project's
  `sidecar-loop-on.asd` fixture.
- [Verbalize-public/ableton-asd-parser](https://github.com/Verbalize-public/ableton-asd-parser),
  an independent Python reader of Live 12 files.
