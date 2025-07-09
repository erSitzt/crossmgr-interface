# Tag Prefix Filter Feature

## Overview

The CrossMgr RFID Interface now includes a powerful tag prefix filtering system that allows you to process only expected rider tags and ignore unwanted RFID reads.

## Purpose

In race environments, you may encounter:

- Test tags during setup
- Spectator badges or equipment tags
- Reader calibration tags
- Tags from other nearby events

The tag prefix filter ensures only legitimate race participant tags are processed for lap timing and statistics.

## How It Works

### UI Controls

- **Tag Filter Text Box**: Enter one or more tag prefixes
- **Filter Enabled Checkbox**: Toggle filtering on/off
- **Set Filter Button**: Apply the current filter settings

### Filtering Logic

1. When a tag is read, the system checks if filtering is enabled
2. If enabled, the tag ID is compared against the configured prefixes
3. Tags matching any prefix are processed normally
4. Non-matching tags are logged but excluded from race statistics

### Multiple Prefix Support

You can specify multiple prefixes separated by commas:

- `RIDER` - matches RIDER001, RIDER002, RIDERX123, etc.
- `RIDER,BIKE` - matches both RIDER* and BIKE* tags
- `1000,2000,ELITE` - matches tags starting with 1000*, 2000*, or ELITE\*

## Usage Examples

### Basic Usage

1. Start the application
2. In the "Tag Filter" field, enter: `RIDER`
3. Check "Filter Enabled"
4. Click "Set Filter"
5. Only tags starting with "RIDER" will be processed

### Multiple Prefixes

1. Enter: `RIDER,BIKE,ELITE`
2. Check "Filter Enabled"
3. Click "Set Filter"
4. Tags starting with RIDER*, BIKE*, or ELITE\* will be processed

### Temporary Disable

1. Uncheck "Filter Enabled"
2. All tags will be processed regardless of prefix

### Clear Filter

1. Clear the text box
2. Click "Set Filter"
3. Filtering is automatically disabled

## Logging

### Processed Tags (Normal)

```
🏷️  Tag: RIDER001                      Time: 17:50:37.786398  Count: 00006    Date: 20250709 Lap 1 [17:50:37.786]
```

### Filtered Tags

```
🚫 Tag: SPECTATOR123                   Time: 17:50:40.123456  Count: 00007    Date: 20250709 [FILTERED #1 - doesn't match prefix 'RIDER'] [17:50:40.123]
```

### Filter Status Messages

```
🔍 Tag filter set to prefix: 'RIDER' (Filter enabled: True)
🔍 Tag filter set to prefixes: 'RIDER', 'BIKE', 'ELITE' (Filter enabled: True)
🔍 Tag filter disabled - all tags will be processed.
🔍 Tag filter cleared - all tags will be processed.
```

## Features

### Case Insensitive

- Filter matching is case-insensitive
- "RIDER" matches "RIDER001", "rider123", "Rider456"

### Real-time Statistics

- Filtered tag count is displayed
- Counter resets when race data is cleared

### Performance

- Filtering adds minimal overhead
- No impact on race timing accuracy

### Memory Efficient

- Filtered tags don't consume rider tracking memory
- Only accepted tags affect race statistics

## Best Practices

### Race Setup

1. Configure filters before starting the race
2. Test with known tags to verify filter settings
3. Use the most specific prefixes possible

### During Race

- Monitor filtered tag count to detect issues
- Tags can be temporarily disabled if needed
- Filter settings can be changed mid-race

### Multiple Categories

For races with multiple categories:

```
ELITE,JUNIOR,WOMEN,MEN
```

### Numeric Prefixes

For numbered rider systems:

```
1000,2000,3000
```

This matches rider numbers 1000-1999, 2000-2999, 3000-3999

## Technical Details

### Implementation

- Uses `String.StartsWith()` with case-insensitive comparison
- Supports comma-separated prefix lists
- Thread-safe operation

### Integration

- Seamlessly integrates with existing lap tracking
- No impact on race statistics or predictions
- Compatible with all CrossMgr protocol features

## Troubleshooting

### No Tags Being Processed

- Check that "Filter Enabled" is unchecked for all tags
- Verify prefix spelling and case
- Ensure tags actually start with the specified prefix

### Too Many Tags Being Filtered

- Check if prefixes are too specific
- Consider using shorter prefixes
- Review actual tag IDs in the logs

### Performance Issues

- Very long prefix lists (>20 items) may impact performance
- Consider using shorter, more inclusive prefixes

## Examples by Use Case

### Cycling Race

```
Filter: BIKE,CYCLE,RIDER
```

### Running Race

```
Filter: RUN,RUNNER,ATHLETE
```

### Triathlon

```
Filter: TRI,ATHLETE,COMP
```

### Multi-Event

```
Filter: EVENT1,EVENT2,ELITE
```

### Testing/Development

```
Filter: TEST,DEV,DEMO
```
