# Product catalog and reported RF channel

`GetProductCatalogAsync` reads `/app/common/core/productModel` with the authenticated appCode 2 contract. Attributed models expose catalog version, distinct model/model-code variants, display name, product code, category, port count, hub/child metadata and data-point definitions. The returned collections are read-only. Raw default-parameter strings and data-point definition payloads are not public APIs.

The contract follows the pinned funkadelic source in [protocol sources](PROTOCOL-SOURCES.md). Duplicate model/model-code pairs and malformed rows are rejected; different model-code variants of one model are retained. Catalog presence does not enable decoding or control for unsupported devices.

`RainPointHub.RfChannel` exposes the positive `recich` discovery value, otherwise unknown. The Windows Hub tools tab shows it read-only. The primary Python integration's channel selector exists, but its setter raises an unsupported-operation error; it is not a working upstream write implementation. The vendor app exposes a selector, whose write contract remains to be established. No RF channel was changed.

Twelve catalog NUnit cases pass on each desktop runtime. Read-only live checks on both targets returned 104 model variants including HTV345FRF; the test hub reported RF channel 1. Its installed firmware was 1.1.1041 and the timer's was 130. Catalog metadata is not hardware validation of those 104 variants.
