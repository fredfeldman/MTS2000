---
name: chip-test-correlation
description: "Use when comparing MTS2000 and STS8300 semiconductor test data, analyzing per-chip CSV correlation, test-item differences, pass/fail yield, limits, leakage, or generating correlation reports. Do not use for Motorola MTS2000 radio programming or EEPROM codeplug work."
---

# Chip Test Correlation

Use this skill for the semiconductor test-data workflow represented by `povxs/chip-test-correlation`.

## Scope

This skill covers comparison of two test-system exports:

- MTS2000 semiconductor test results
- STS8300 semiconductor test results
- Per-chip and per-test-item comparisons for the same device family
- CSV parsing, statistics, limits, pass/fail analysis, and HTML/Excel/report output

This is unrelated to the Motorola MTS2000 radio programmer in this workspace. Do not infer radio EEPROM layouts, SB9600/SBEP protocol behavior, codeplug blocks, or Toolproof algorithms from this reference.

## Expected CSV concepts

Treat these fields as structural metadata when present:

- `SITE_NUM`
- `PART_ID`
- `PASSFG`
- `SOFT_BIN`
- `T_TIME`
- `TEST_NUM`

Other columns are test items. Preserve their source order for report presentation. Pair rows by `PART_ID`, not by row position, and report chips that exist in only one dataset.

For each test item, retain:

- item name
- unit
- lower and upper limits
- values by part ID
- missing-value counts
- pass/fail information when available

## Analysis workflow

1. Validate both input paths and identify the source system for each file.
2. Parse headers, units, limits, and data rows without silently dropping malformed records.
3. Build a test-item map for each system, excluding structural metadata columns.
4. Compute common chips, MTS-only chips, STS-only chips, common items, and system-only items.
5. Pair common chips by `PART_ID`.
6. For each common item and common chip, calculate:
   - MTS value
   - STS value
   - absolute difference `MTS - STS`
   - relative difference against the specification window when valid
   - direction: positive, negative, zero, or unavailable
7. Aggregate item statistics: count, mean, median, standard deviation, minimum, maximum, and missing values.
8. Compare limits independently from measured-value correlation. Different limits can explain yield differences without proving a hardware measurement difference.
9. Group findings by meaningful categories such as leakage, open/short, resistance, address/enable, trim, VBG, IZTC, and VREF.
10. Produce machine-readable JSON plus human-readable HTML or Excel output when requested.

## Calculation rules

Use the specification window only when both limits are numeric and the window is non-zero:

`relative_difference_percent = (MTS_value - STS_value) / (upper_limit - lower_limit) * 100`

Do not divide by a measured value near zero. Keep absolute difference and relative difference separate. Mark unavailable values as missing rather than treating them as zero.

For a system summary, report:

- total chips
- pass count
- fail count
- pass rate
- average test time
- test-item count
- common-item count
- system-only item counts

## Interpretation guidance

Correlation results are evidence, not proof of root cause.

- Large leakage differences can arise from PMU range, measurement timing, fixture layout, parasitics, connection method, or resolution.
- Trim-related differences may come from trim algorithms or measurement conditions.
- Different limits may represent different judgment logic rather than different physical behavior.
- Results from different chip lots or dates are not a controlled cross-system comparison.
- A small average difference can conceal outliers; include per-chip extremes and distribution statistics.
- Compare the same chip batch on both systems before concluding that one system is inaccurate.

## Reporting requirements

A useful report should include:

- input files and test dates
- system names and versions when available
- chip counts and common-chip count
- common, MTS-only, and STS-only test items
- yield and average test-time comparison
- largest positive and negative differences
- largest relative differences
- items with different limits
- category-level summaries
- per-chip detail for important outliers
- explicit data-quality limitations

Avoid presenting a relative percentage for a zero-width or missing specification window. Clearly distinguish missing data, nonnumeric data, and measured zero.

## Verification checklist

Before accepting an analysis:

- Confirm chip matching uses `PART_ID`.
- Confirm duplicate part IDs are detected and reported.
- Confirm columns with different ordering are aligned by item name.
- Confirm units and limits are retained per source system.
- Confirm pass/fail counts match the source rows.
- Confirm common-item and system-only counts reconcile.
- Test missing values, blank limits, zero-width limits, malformed numbers, and duplicate IDs.
- Verify generated JSON can be consumed independently of the HTML report.
- Preserve the original CSV files and record the analysis inputs in the report metadata.
