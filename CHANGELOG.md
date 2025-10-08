# Changelog

<!-- There is always Unreleased section on the top. Subsections (Add, Changed, Fix, Removed) should be Add as needed. -->
## Unreleased

## 5.0.0 - 2025-10-08
- Use `CloudNative.CloudEvents` as a base type for `CloudEvent`
- [**BC**] Remove `CloudEventDto` module and type, with other sub types
- Add `CloudEvent` functions
    - `toHttpContent`
    - `toJson`

## 4.0.0 - 2025-10-08
- [**BC**] Require `eventType` for a `CloudEvent`

## 3.2.0 - 2025-10-08
- Add `CloudEventDto.fromCloudEventResult` function

## 3.1.0 - 2025-10-07
- Add types
    - `CloudEvent`
    - `CloudEventDto`

## 3.0.0 - 2025-03-18
- [**BC**] Use net9.0

## 2.0.0 - 2024-01-11
- [**BC**] Use net8.0
- Fix package metadata

## 1.0.0 - 2023-09-11
- Initial implementation
