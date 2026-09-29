# Permanent town resident lifecycle harness

`python3 scripts/check-town-residents.py` compiles and executes the production
`TownServicePopulation`, `RemoteTownResidents`, and resident state source files.
`--source-root` is available to validate an integrator's in-progress source tree
without copying or editing production files in a worker checkout.

Engine objects, wall clock, map availability, original service presence, asset
readiness, station rendering, and visit input are explicit dependency doubles.
The test observes create/dispose counts, input eligibility, alpha requests,
original animation selection and age, author-only floor call intent, published poses,
author election and expiry. Codec entry points throw if accidentally reached;
wire serialization has separate golden-vector tests.

Covered flows:

- all three residents remain between visits and through idle map browsing;
- default-on/off and repeated enable/map lifecycle changes;
- an opted-out viewer sees only a real remote visitor's station;
- stale visits disappear using the same cutoff as resident author election;
- lower fresh player wins independently of arrival order, local preference,
  departure, opt-out or missing resident records;
- missing native art preserves native affordances and prevents invisible input;
- asynchronous decoration readiness gates alpha and authority advertisement;
- missing prefab retries are bounded and recover without reopening the map;
- observer environment changes cannot replace authoritative floor or pose;
- author departure requests author placement again; actual station cache invalidation
  is covered by `check-town-service-setting.py` using the full production station class;
- shared frame transforms and animation extrapolation are executed by production.

Four compiled mutations must fail at runtime: visitor-only lifetime, writing the
viewer's floor on a follower, ignoring asset readiness, and never expiring authors.
This is a production control-flow test, not a Unity renderer, network transport,
Addressables loader, or headset appearance test. Quaternion composition and XR
input events remain engine boundaries; physical geometry is covered separately by
`check-town-service-setting.py` and Unity/headset validation. The population harness
uses a station double and by itself cannot establish correct cache invalidation
inside `Station.RefreshEnvironment`, lighting lifetime, or native renderer behavior.
