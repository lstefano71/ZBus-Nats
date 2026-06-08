# Adapter factory per root + dispatch helper

Adapters are registered globally as factories (`Func<IAdapter>`), not instances. When a root is created via `zbus_init`, the kernel instantiates each registered adapter factory and calls `Initialize(poster, registry)` with that root's services. Each root gets independent adapter instances — no shared state between roots.

Adapter verb exports (e.g., `zbus_echo_timer`) use a kernel-provided `Dispatch<TAdapter>` helper that handles: string reading from `<0T1`, root lookup from the first dot-segment, adapter resolution, try/catch → rc mapping. This keeps verb exports as thin one-liner dispatchers.

The alternative was shared adapter instances across roots, but that breaks the independence guarantee and forces adapters to manage multi-root state internally.
