# MB489 native presentation compression

The parity review found a transport capacity gap: complete original card material snapshots
can occupy many bounded presentation events. Keeping every original float is required; dropping
material channels to fit one event would introduce another visual divergence.

The additive message16 / record64 envelope compresses the complete original packet with Deflate.
It is enabled by the integration queue, only for inputs of at least 512 bytes and at least 64
bytes saved after the checksum. Incompressible packets keep the original envelope48 path.
The old grammar and direct encoder default remain unchanged.

Each compressed record carries sequence:u64, original message:u8, original length:u16, compressed
length:u16 and offset:u16, followed by at most 197 data bytes. The compressed body ends with the
original bytes' CRC32. Four records fit in 862 bytes, below the existing 864-byte event limit.
Each stream retains its existing bounded assembler and sequence watermark. Before routing or
allocation, every record must agree on its original type, sequence and sizes. Mixing compressed
and legacy chunks of the same generation retires that generation. No partial state is published.

Expansion allocates only the declared length within the original stream's maximum. A read of
one extra byte rejects a deflate expansion beyond that length. Truncated output, corrupt CRC,
invalid deflate and an unexpected original message are rejected before the original codec runs.
The payload codec remains responsible for its own semantic validation, including finite values.

## Validation

The source-linked wire suite passed **250,443 assertions**, including an independent raw-deflate
golden packet, every truncated packet/body, CRC corruption, an expansion bomb, wrong stream,
reordered pages, duplicates, mixed encodings and native slot routing. Existing paging fixtures
now use seeded incompressible payloads so they retain their original page-count assertions while
the production queue uses compression.

Measurements use real `CardAppearanceCodec` output with all twenty native roles, material
properties and eight groups, rather than an unrelated repeated-byte buffer:

| Fixture | Original bytes | Total wire bytes | Pages | Wire / original |
| --- | ---: | ---: | ---: | ---: |
| Eight cards, repeated native values with per-card variation | 10,246 | 1,147 | 2 | 11.2% |
| Thirty-two cards, repeated values with per-card variation | 40,966 | 4,012 | 5 | 9.8% |
| Thirty-two cards, independently randomized material floats | 40,966 | 29,063 | 34 | 70.9% |

These are synthetic source-valid fixtures, not headset measurements. All reconstructed bytes
match the original, including every material float. Compression improves repeated native UI
output substantially; it does not establish a delivery-latency bound for arbitrary entropy.
The strict build compiled this lane without diagnostics but was blocked by unrelated prompt
integration types and a source-owner guard namespace error; the integrator's final build remains
the release gate.
