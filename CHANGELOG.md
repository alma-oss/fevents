# Changelog

<!-- There is always Unreleased section on the top. Subsections (Add, Changed, Fix, Removed) should be Add as needed. -->
## Unreleased
- Add the CloudEvents spec form next to the strict one
    - `EventSource.tryParseReference` (any URI-reference, relative included) and `EventSource.ofSoftwareComponent` (`instance@environment`, new dependency `Alma.EnvironmentModel`)
    - `Envelope` (`id`, `type`, `source` required; `subject`, `dataschema`, `time`, extension attributes optional) and `CloudEvent.createFromEnvelope`
    - `ExtensionName` / `ExtensionValue` (string, timestamp, integer, boolean) and `CloudEvent.tryExtension`
    - `CloudEvent.tryData` to read the typed data back
    - `CloudEvent.parse` accepts a relative `source` and a missing `dataschema` and carries extension attributes; `MissingDataSchema` is raised only for a present but invalid one

## 7.1.0 - 2026-08-18
- Update dependencies
    - `Alma.Kafka` 30.2

## 7.0.0 - 2026-01-29
- [**BC**] Use net10.0

## 6.0.0 - 2025-11-28
- Move repo

## 5.2.0 - 2025-10-20
- Update dependencies

## 5.1.0 - 2025-10-08
- Add `CloudEvent.mapDataResult` function

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
