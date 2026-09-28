# Water usage and event history

`GetTimerWaterUsageAsync` reads daily or monthly HTV345FRF totals for a selected zone. `GetEventsAsync` reads one filtered event page for a home. These are explicit cloud reads; they send no valve or configuration commands and do not start polling.

```csharp
var daily = await client.GetTimerWaterUsageAsync (hub, timer.Address, 1,
    RainPointUsagePeriod.Day, new DateTime (2026, 9, 1), new DateTime (2026, 9, 24));

var query = new RainPointEventQuery
    {
    HubId = hub.Id,
    Address = timer.Address,
    Zone = 1,
    Code = 1, // Watering records; omit for all event types.
    Limit = 50
    };
var events = await client.GetEventsAsync (hub.HomeId, query);
```

## Dates, amounts and missing data

Usage inputs are inclusive home-calendar dates at midnight, with `DateTimeKind.Unspecified`. Daily requests are bounded to 30 days; monthly requests to one calendar year, matching the app's query windows. Monthly response dates identify the first day of their month. Do not convert them through the computer's local timezone or assume a partial-month query is a prorated total; that boundary behavior remains unverified.

The cloud returns sparse buckets. The client returns only those buckets, sorted by date. Missing/null or negative readings become nullable litres; reported zero stays zero. Duplicate, invalid or out-of-range bucket dates reject the response. The vendor chart's `val / 10` conversion is used for litres. No missing day/month is manufactured as zero, and no completeness or retention guarantee is implied.

Events retain both the cloud Unix-millisecond timestamp (`CloudTimestamp`) and separately parsed device-local time (`ReportedLocalTime`, Unspecified), plus the reported timezone label. These times can differ. Query `Begin`/`End` bounds use cloud timestamps. The client does not reinterpret the local time as a UTC instant.

Known detail rules produce nullable water litres, duration, work/control mode codes, exception code, online state and operator. Event kinds 1–5 identify watering, water usage, water control, hub status and sub-device power-on records. Other event codes remain numeric with `Kind=Unknown`. Unknown, duplicate or malformed detail rules set `HasUninterpretedDetails`; known independent details remain available. Unknown mode/fault codes are not assigned guessed descriptions. Protocol payloads and rule bags are not exposed.

## Filtering and pages

`RainPointEventQuery` supports hub ID, RF address, zone, event code, beginning/end and a limit of 1–50. Address requires a hub; zone requires an address. Omit device filters for the home's event stream. Home-wide results can include unsupported product families; unrecognized details remain uninterpreted.

`IsLimitReached` means only that the page filled its requested limit. To request older events, use `OldestTimestamp` as the next query's `End`, as the Android app does. Keep a set of event IDs when combining pages, detect a page that makes no progress, and stop rather than looping. The service has no verified ID tie-break cursor: timestamp ties, concurrent inserts, retention and boundary inclusivity are not guaranteed. The client neither loops automatically nor silently discards duplicate events.

## Protocol evidence and validation

The contract was traced from RainPoint Home 1.19.1065 and its vendor-hosted [chart application](https://region3.homgarus.com/ui2/record), inspected on 24 September 2026. The downloaded chart bundle was `view-record-DqjRmUx4.js`. No vendor code or private account captures are included in this repository.

| Read | Contract |
| --- | --- |
| Daily usage | GET `/app/iot/log/waterAmount/day/list`, `code=0`, `mid`, `addr`, `port`, `startDate`/`endDate` as YYYYMMDD; response `ymd` and `val` |
| Monthly usage | GET `/app/iot/log/waterAmount/month/list`, same query fields; response `ym` and `val` |
| Events | GET `/app/device/event/list`, `hid`, `size`, optional `mid`, `addr`, `port`, singular `code`, `begin`/`end` in Unix milliseconds |

Forty new offline NUnit cases pass on net472 and net10.0. They cover exact paths/filters, quoted numbers, conversion, zero/missing/invalid readings, date boundaries, malformed envelopes/metadata, unknown/duplicate details, immutable results, timestamp separation, page limits and cancellation. At completion of the library history slice, the offline suite passed 350 library/dashboard plus 12 Windows cases per framework: **724 passes**. See the [test README](../tests/README.md) for current totals.

Explicit read-only NUnit fixtures passed on both runtimes, including daily/monthly reads and a second event page. The first exploratory read returned 10.4 L; later fixture runs returned 15.1 L. The one-off app comparison showed 4.7 L for 24 September and 15.1 L for September. The latest app event and member-account client result both showed local start 10:52:20, duration 61 seconds and 4.7 L. This work sent no watering command, and these observations do not independently calibrate volume or prove when the cloud updates its totals.

An invited second account accepted the home invitation inside **HOME Management**. Owner-account client reads completed while that member account stayed signed into the app and subsequently loaded fresh charts and events. A separate member-account client read also passed. This establishes concurrent read access by distinct accounts for this home, not simultaneous logins to the same account or all member write permissions. Invoking the client with the app's own account still displaced that app login.

App navigation was a one-off BlueStacks inspection, not a project workflow dependency. The app was returned to its signed-out screen, the account field cleared and the Android reservation released. Repeatable live checks use NUnit and NUnit3TestAdapter; see [test instructions](../tests/README.md#usage-and-event-history).

History is also available in the [Windows workbench](WINDOWS-APP.md#usage-and-event-history), with daily/monthly tables, sparse-data summaries, event filters/details and bounded manual paging. That UI addition was tested offline on both frameworks; it does not add independent hardware evidence. Email export, retention guarantees, DST/calendar-boundary behavior and exhaustive event-detail coverage remain outside this slice.

## Whole-timer events in a zone query

A live zone-2 query returned event code 5 with port 0: a timer power-on event. The client now accepts that specific whole-timer event in an otherwise correctly addressed zone query, preserves zone 0, and labels it **Whole timer** in the Windows history table. It does not relabel it as zone 2. Other zero-zone events or mismatched zone/hub/address filters remain rejected; six regression cases cover all three zones and invalid alternatives.

After this correction, `CompletionLiveTests` read settings, usage and bounded event pages for zones 1, 2 and 3 on both net10.0 and net472 on 24 September 2026. No valve or configuration commands were sent. Initial protocol failure and later transient network failures are retained alongside successful reruns.
