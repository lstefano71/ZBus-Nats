# Synthetic Closed event on teardown

When `Close` is called on a ZBus object:

1. A synthetic `Closed` event is posted with general delivery (bubbles to nearest active waiter)
2. Remaining targeted events are discarded (their specific consumer is gone — programming error)
3. No new events are accepted for that name
4. The name is freed once the Closed event is consumed (or immediately if no waiter exists)

This gives a clean lifecycle signal: an event loop watching a parent object naturally receives `Closed` when a child is torn down, without needing to special-case forced close. Real Conga does not behave this way (events can be silently lost on close), which we consider a design deficiency.
