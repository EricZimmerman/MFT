# DFIR Artifact Museum samples

From https://github.com/AndrewRathbun/DFIRArtifactMuseum/tree/main/Windows/NTFSArtifacts (MIT license):

- `Boot/<OS>/<system>/$Boot`
- `LogFile/Win2012R2/StolenSzechuan/$LogFile.gz`: gzipped (18 MB -> 1.6 MB); the tests decompress it
- `Secure_$SDS/Win2012R2/StolenSzechuan/$Secure_$SDS`
- `J/Win2012R2/StolenSzechuan/$J`: has records after zeros in the middle of a page, which earlier versions skipped (38 records)

Expected values in the tests come from independent decoders (Python) and the Rust `mft` crate (pymft).
