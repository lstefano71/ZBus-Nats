# Post-time delivery flag: general vs targeted

Events posted to the ZBus kernel carry a delivery mode flag set at post-time by the adapter:

- **General delivery**: the event is delivered to the deepest waitpoint with an active waiter, bubbling up from the target object toward the root. If no waiter is active, the event queues at the target.
- **Targeted delivery**: the event is only visible to a waiter on the exact matching waitpoint. Never bubbles. Used for request/reply mailboxes.

This solves the race condition where an event arrives before the specific waiter is ready: targeted events park safely at their waitpoint until claimed, while general notifications flow to whoever is listening. The adapter knows the intent at post-time (a response to a specific request vs. a general notification), making the delivery deterministic without magic timeouts or steal semantics.

## Considered Options

- **Bubble-on-timeout**: event parks at target, bubbles after N ms if unclaimed. Rejected — arbitrary timeouts are fragile and create hard-to-reproduce race conditions.
- **Copy to all levels**: every matching waitpoint gets a copy. Rejected — double-processing risk and breaks the "consume once" invariant.
- **Steal at wait-time**: parent waiters can claim child events. Rejected — breaks most-specific-wins predictability.
