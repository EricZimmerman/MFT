# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

`netstandard2.0` NuGet libraries for NTFS metadata, each its own package: **MFT** ($MFT), **Boot** ($Boot), **I30** (directory index allocations), **Secure** ($Secure:$SDS, plus $SDH/$SII), **Usn** ($UsnJrnl:$J), **O** ($ObjId:$O) and **MFT.LogFile** ($LogFile; the project, assembly and namespace are `LogFile`, but the `Logfile` id on nuget.org is someone else's). MFT.LogFile depends on MFT (it decodes logged records with MFT's attribute parsers), so they are released together. The main consumer is MFTECmd (and MFTExplorer); MFTECmd uses the packages, so library changes that it needs to follow go there as a package bump. Input may be corrupt or hostile.

## Commands

The SDK is pinned in `global.json` (.NET 10). Libraries target `netstandard2.0`, tests `net10.0`; all `LangVersion 10` (no `init`, no spans without System.Memory). Tests use NUnit 5 + FluentAssertions 7 (pinned to `[7.2.2,8.0.0)`, since 8.x requires a commercial license).

```bash
dotnet build -c Release                                          # builds MFT.slnx
dotnet test -c Release                                           # CI; CorpusTests is [Explicit] and skipped
dotnet test -c Release --filter "FullyQualifiedName~CorpusTests" # every $MFT in ../Corpus/MFTs (or ../MFTs, or MFT_CORPUS), slow
dotnet test --filter "FullyQualifiedName~RecordRobustnessTests"  # one fixture
dotnet pack -c Release
```

## MFT

- `MftFile.Load` -> `Mft`: reads FILE records of the size at 0x1C of the first record (a power of two, 0x100-0x10000), sorts them into `FileRecords` (in use), `FreeFileRecords`, `BadRecords` (BAAD), `UninitializedRecords`, joins extension records' attributes to their base record, and builds the directory maps for `GetFullParentPath`/`GetDirectoryContents`. Keys are `"{entry:X8}-{seq:X8}"`; deleted records use sequence - 1. `GetFullParentPath` stops on cycles. `Mft.ParseErrors` is for records that could not be processed at all.
- Memory: a loaded $MFT is about 1.6x its size (it was 2x). Timestamps are kept as raw FILETIMEs and converted when read (`FileTimes`, the same UTC values as before). `FileRecord.FixupData` is built when read from the raw fixup array. `FileRecord.ParseErrors` is created on first use, so use `HasParseErrors` to check without creating it. Keep per-record objects small: there are millions of them.
- `MftEntryInfo.MftEntryNumber` is a 48 bit `ulong` (the high 16 bits are at bit 32). It used to be a `uint` with those bits multiplied by 2^24.
- `IndexRoot`: entries are parsed from their start, so each `IndexEntry` has its own `MftReference` (the file it is for) as well as its key's `ParentMftRecord`. `IndexRoot.MftRecord` is the first entry's reference (kept for compatibility). The public `IndexEntry(byte[])` still takes an entry from 8 bytes in; `IndexEntry.FromEntry` takes a whole one.
- `FileRecord`: fixups (sector ends at 510, 1022, ...), header, then attributes until the end marker, within the allocated size. Deleted records can have a used size (0x18) that ends before intact attributes, so parsing goes past it, but past the used size anything invalid just stops quietly. NTFS 3.0 records (fixup array at 0x2A) have no entry number at 0x2C; it comes from the offset. `Offset` is a `long` (MFTs over 2 GB).
- Error model: `FileRecord.ParseErrors` for a bad header/fixup array, bad attribute sizes, attributes that throw, and unknown types; the attributes that parse are kept. Attribute parsers must bound every length and never loop without moving forward (IndexRoot entries, EA chunks, data runs, attribute lists all had hangs or inversions).
- `SkSecurityDescriptor`: part sizes come from the data (ACL size at +2, SID = 8 + 4 x subauthorities), not from the gaps between offsets. `XAclRecord` ACE sizes are UInt16. `AceRecord.Sid` handles object ACEs (flags + up to two GUIDs before the SID). `Helpers.ConvertHexStringToSidString` uses the subauthority count, so callers can pass longer buffers.

## Other formats

- **Boot**: sectors per cluster over 0x80 is 2^(256 - value).
- **I30**: 4096 byte INDX pages; fixups applied to the page; entries from 0x18 + data offset to 0x18 + data size, slack after that (slack entries found by `FileRecord.GetSlackFileEntries`, deduplicated by MD5). Only a bad first page throws; later bad pages go to `ParseErrors`.
- **Secure ($SDS)**: 256 KB data blocks each followed by a 256 KB mirror. Each security id is returned once from the data blocks; a mirror copy that differs is added with `FromMirror`. Entries are validated by their offset field (= their position); freed entries (id 0) are skipped. `LastOffset` is a leftover static.
- **Usn**: buffered; zeros are skipped 8 bytes at a time (records are 8 byte aligned and can start mid-page after zeros); junk skips to the next page. V2 and V3 (128 bit references, NTFS uses the low 64 bits) are parsed, V4 (range tracking) skipped by length. Bad timestamps set `InvalidTimestamp` and a `ParseErrors` entry. `UsnFile.Load` finds the start of the data in a sparse $J.
- **O**: INDX pages of $O entries (0x58 bytes). Entries are collected across all pages (they used to be reset per page).

## $LogFile (LogFile project, package MFT.LogFile)

- Layout: two restart pages ("RSTR"/"CHKD", restart area at the restart offset: current LSN, sequence number bits, log size, log page data offset, clients), then 2 tail pages (LFS 1.1, Windows 7 and older) or 32 fast pages (LFS 2.0) holding copies of recent log pages, then the circular log area. All pages have fixups (`Fixups.Apply`).
- LSN = sequence number (high `SequenceNumberBits` bits) and file offset / 8 (the rest). A record is accepted only where its own LSN maps (`LsnToOffset`); that is what keeps stale data out while still finding records from earlier passes (older sequence numbers).
- Copies: a copy replaces the page it copies (found from its last end LSN) when it is newer: LFS 2.0 by the last LSN field at 0x08 (a copy with the same last LSN but a higher last end LSN is not newer: its last record continues into the next page), LFS 1.1 by the last end LSN (0x08 is the file offset in tail pages). Copies also supply pages past the end of a $LogFile that was cut short (the log size comes from the restart area).
- Records: walked from each page's data offset, continuing across pages (client data resumes at the next page's data offset; a record never starts in the last 0x30 bytes of a page). Then every 8 byte position of every page is checked for a header whose LSN maps to it, because records from earlier passes can start mid-page after leftovers of a newer record. Records are returned by LSN.
- `NtfsLogRecord` (client records): 0x20 byte header (redo/undo ops, offsets, lengths, target attribute, LCN count, record/attribute offsets, cluster block offset, target block size, target VCN), LCNs, redo/undo data. Data can be absent when only a length is logged (ZeroEndOfFileRecord, zero-filled updates): that is not an error. `TargetMftEntry` = (VCN x cluster size + cluster block offset x 512) / record size for operations on file records (cluster size defaults to 4096; pass the real one). Decoded: InitializeFileRecordSegment -> `FileRecord` (parsed without fixups: `FileRecord(..., applyFixups: false)`), Create/DeleteAttribute -> attribute, Add/DeleteIndexEntry -> `LoggedIndexEntry` ($FILE_NAME key), UpdateFileName -> `LoggedFileNameUpdate`.
- Checkpoints: a client restart record's data is the NTFS restart area (`LogRecord.Restart`: version, and the LSNs and lengths of the table dumps of the same checkpoint). `OpenAttributeTableDump` redo data is a restart table (0x18 byte header: entry size, count, ...; an entry is allocated when it starts with 0xFFFFFFFF). Windows 8+ entries are 0x28 bytes (bytes per index buffer, type, dirty, file reference, LSN of open, pointer); Windows 7 ones are 0x2C bytes (pointer, file reference, LSN, dirty, type, name, bytes per index buffer). `AttributeNamesDump`: (table offset, name length, UTF-16 name, NUL) entries. `OpenNonresidentAttribute`: the redo data is an entry, the undo data its name (Windows 8+; Windows 7 has none, so the name comes from a later dump of the same attribute, else it is null).
- `NtfsLogRecord.TargetOpenAttribute`: `TargetAttribute` is an entry's byte offset in the open attribute table. A record uses the newest definition of that offset (dump or open record) at or before its LSN, else the oldest one (an attribute that was open before the log starts). This only applies within a stretch of the log: a restart area without tables (a mount or clean dismount) and gaps of more than 64 pages (records left from older passes) start a new one. A record with no definition in its own stretch takes the nearest one elsewhere, with `TargetOpenAttributeInferred`. File record operations resolve to $MFT:$DATA. Checked over six Museum logs and dfir_ntfs's test logs (Windows 7, 10, downgraded): every $I30 index entry added or deleted through a resolved target has that directory as its parent, and against the $MFT the resolved files have that non-resident attribute (except directories whose index allocation was removed since). Inferred targets were right in every checkable case.
- `LogRecord.TransactionFirstLsn`: the first record of the transaction, by following `ClientPreviousLsn`. Transaction ids are reused slots; every group has one id.
- Checked against dfir_ntfs (msuhanov, GPL; used only as a black box, never copied): over six DFIR Artifact Museum logs and its own test logs (LFS 1.1 and 2.0), every record it finds is found with identical fields; this parser also finds records it misses (older passes, the first page of the log area). Decoded index entries and file records were checked against the $MFT of the same system.

## Tests

`MFT.Test` (net10.0) references every library; `TestFiles/` is copied to the output.
- `MftTests`: the real $MFT samples (counts, paths, timestamps, attribute lists), values checked against the Rust mft crate (pymft), which has its own quirks: it reads past 48 byte $STANDARD_INFORMATION, names the allocated size `logical_size`, and reports base record sequence numbers as 0.
- `RecordRobustnessTests` (with `RecordBuilder`) and `MftFileTests`: built and damaged records and $MFT files. Add regression tests here.
- `OtherFormatTests`: $Boot, $I30, $SDS, $J, $O and SIDs/ACEs, with built data where there are no samples.
- `LogFileTests` (with `LogFileBuilder`, which builds LFS 2.0 logs): a real Server 2012 R2 $LogFile (gzipped in `TestFiles/DFIRArtifactMuseum/LogFile`), records spanning pages, fast copies, pages past the end of the file, old records mid-page, torn pages, decoding.
- `TestFiles/Generated`: made with ntfsprogs (see its README). `TestFiles/DFIRArtifactMuseum`: MIT samples from the DFIR Artifact Museum (see its README). `CorpusTests`: a local `MFTs` folder in `Corpus/` next to the repo (or directly next to it, or `MFT_CORPUS`), never copied into the repo.

## Versioning and release

- MinVer sets the version of every package from the git tag (bare tags like `2026.10.0`); there is no `<Version>` in the csprojs.
- To release: push to `master`, then `git tag <ver> && git push origin <ver> && gh release create <ver> --verify-tag --generate-notes`. Then update MFTECmd's package references.
- `.github/workflows/publish.yml` (identical to GuidMapping's) packs, validates and tests on PRs and pushes to `master`. When a GitHub Release is published, it pushes to NuGet.org using trusted publishing (`NuGet/login` with the `NUGET_USER` repo variable, `EricRZimmerman`).
- Dependabot (`.github/dependabot.yml`) opens monthly PRs for GitHub Actions and NuGet updates.
