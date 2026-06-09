using ZBus;
using ZFormat;

namespace ZBus.Tests;

/// <summary>
/// Regression tests for the leaf waitpoint delivery bug.
/// Before the fix, events posted to a leaf target while the waiter was between
/// TryReceive calls would be routed to root and lost to the leaf waiter.
/// </summary>
public class WaitpointTreeLeafDeliveryTests
{
    [Fact]
    public void LeafWait_ReceivesEventPostedBetweenWaits()
    {
        // Event arrives between two Wait calls on the same leaf — must not be lost.
        var tree = new WaitpointTree("R");

        // First wait with concurrent producer (establishes pattern)
        var producer = Task.Run(() =>
        {
            Thread.Sleep(50);
            tree.PostGeneral("R.sub1", new BusEvent("R.sub1", "Data", ZValue.EmptyNumeric, false));
        });

        var evt = tree.Wait("R.sub1", 2000);
        Assert.NotNull(evt);
        Assert.Equal("R.sub1", evt.ObjectName);
        producer.Wait();

        // Post while NO waiter is active (the critical window)
        tree.PostGeneral("R.sub1", new BusEvent("R.sub1", "Data", ZValue.EmptyNumeric, false));

        // Must be found at leaf
        var evt2 = tree.Wait("R.sub1", 100);
        Assert.NotNull(evt2);
        Assert.Equal("R.sub1", evt2.ObjectName);

        tree.Dispose();
    }

    [Fact]
    public void LeafWait_ManyMessagesNeverLost()
    {
        // Sequential post+wait on leaf must work for hundreds of messages.
        var tree = new WaitpointTree("R");
        const int messageCount = 500;

        int received = 0;
        for (int i = 0; i < messageCount; i++)
        {
            tree.PostGeneral("R.leaf1", new BusEvent("R.leaf1", "Data", ZValue.EmptyNumeric, false));
            var evt = tree.Wait("R.leaf1", 1000);
            if (evt == null) break;
            received++;
        }

        Assert.Equal(messageCount, received);
        tree.Dispose();
    }

    [Fact]
    public void LeafWait_ConcurrentProducerConsumer()
    {
        // Producer posts rapidly, consumer waits on leaf in a loop.
        var tree = new WaitpointTree("R");
        const int messageCount = 1000;

        int received = 0;

        var producer = Task.Run(() =>
        {
            for (int i = 0; i < messageCount; i++)
            {
                tree.PostGeneral("R.fast", new BusEvent("R.fast", "Data", ZValue.EmptyNumeric, false));
                if (i % 100 == 0) Thread.Sleep(1);
            }
        });

        while (received < messageCount)
        {
            var evt = tree.Wait("R.fast", 2000);
            if (evt == null) break;
            received++;
        }

        producer.Wait();
        Assert.Equal(messageCount, received);
        tree.Dispose();
    }

    [Fact]
    public void RootWait_ReceivesChildEvents()
    {
        // Root wait must find events buffered at children.
        var tree = new WaitpointTree("R");

        // Post to child — no waiter active anywhere
        tree.PostGeneral("R.child1", new BusEvent("R.child1", "Data", ZValue.EmptyNumeric, false));

        // Wait on root should find it via descendant scan
        var evt = tree.Wait("R", 100);
        Assert.NotNull(evt);
        Assert.Equal("R.child1", evt.ObjectName);

        tree.Dispose();
    }

    [Fact]
    public void RootWait_MultipleChildrenAllDelivered()
    {
        // Events from different children all arrive at root waiter
        var tree = new WaitpointTree("R");
        const int perChild = 100;
        string[] children = ["R.a", "R.b", "R.c"];

        foreach (var child in children)
        {
            for (int i = 0; i < perChild; i++)
            {
                tree.PostGeneral(child, new BusEvent(child, "Data", ZValue.EmptyNumeric, false));
            }
        }

        int total = 0;
        while (true)
        {
            var evt = tree.Wait("R", 100);
            if (evt == null) break;
            total++;
        }

        Assert.Equal(perChild * children.Length, total);
        tree.Dispose();
    }

    [Fact]
    public void SwitchFromRootToLeaf_EventNotLost()
    {
        // THE critical scenario: wait on root, stop, event arrives, switch to leaf wait.
        var tree = new WaitpointTree("R");

        // Wait on root first (times out — nothing posted)
        var nothing = tree.Wait("R", 10);
        Assert.Null(nothing);

        // Event arrives while nobody is waiting — buffers at target "R.sub1"
        tree.PostGeneral("R.sub1", new BusEvent("R.sub1", "Data", ZValue.EmptyNumeric, false));

        // Now wait on leaf — must find it
        var evt = tree.Wait("R.sub1", 100);
        Assert.NotNull(evt);
        Assert.Equal("R.sub1", evt.ObjectName);

        tree.Dispose();
    }

    [Fact]
    public void SwitchFromLeafToRoot_EventNotLost()
    {
        // Wait on leaf, stop, event arrives at a different child, switch to root.
        var tree = new WaitpointTree("R");

        // Wait on leaf (times out)
        var nothing = tree.Wait("R.sub1", 10);
        Assert.Null(nothing);

        // Event arrives for a DIFFERENT child — buffers at "R.sub2"
        tree.PostGeneral("R.sub2", new BusEvent("R.sub2", "Data", ZValue.EmptyNumeric, false));

        // Now wait on root — should find it via descendant scan
        var evt = tree.Wait("R", 100);
        Assert.NotNull(evt);
        Assert.Equal("R.sub2", evt.ObjectName);

        tree.Dispose();
    }

    [Fact]
    public void LeafWait_DeepHierarchy()
    {
        // Test with deeper names: R.conn.sub1
        var tree = new WaitpointTree("R");

        // Post to deep target
        tree.PostGeneral("R.conn.sub1", new BusEvent("R.conn.sub1", "Data", ZValue.EmptyNumeric, false));

        // Wait on exact leaf
        var evt = tree.Wait("R.conn.sub1", 100);
        Assert.NotNull(evt);

        // Post again, wait on intermediate ancestor
        tree.PostGeneral("R.conn.sub1", new BusEvent("R.conn.sub1", "Data", ZValue.EmptyNumeric, false));
        var evt2 = tree.Wait("R.conn", 100);
        Assert.NotNull(evt2);
        Assert.Equal("R.conn.sub1", evt2.ObjectName);

        tree.Dispose();
    }

    [Fact]
    public void ActiveWaiter_StillGetsDirectDelivery()
    {
        // When a waiter IS active, PostGeneral delivers directly (no scan needed).
        var tree = new WaitpointTree("R");
        const int messageCount = 200;

        int received = 0;
        var producer = Task.Run(() =>
        {
            Thread.Sleep(20); // Let consumer enter Wait first
            for (int i = 0; i < messageCount; i++)
            {
                tree.PostGeneral("R.sub", new BusEvent("R.sub", "Data", ZValue.EmptyNumeric, false));
            }
        });

        while (received < messageCount)
        {
            var evt = tree.Wait("R.sub", 2000);
            if (evt == null) break;
            received++;
        }

        producer.Wait();
        Assert.Equal(messageCount, received);
        tree.Dispose();
    }

    [Fact]
    public void TargetedEvent_NotVisibleToAncestorScan()
    {
        // Targeted events must only be visible to exact-match Wait.
        // Root Wait scanning descendants must NOT pick them up.
        var tree = new WaitpointTree("R");

        // Post a targeted event to a leaf
        tree.PostTargeted("R.reply1", new BusEvent("R.reply1", "Reply", ZValue.EmptyNumeric, true));

        // Root wait should NOT find it (descendant scan skips targeted channel)
        var evt = tree.Wait("R", 50);
        Assert.Null(evt);

        // Exact leaf wait SHOULD find it
        var evt2 = tree.Wait("R.reply1", 50);
        Assert.NotNull(evt2);
        Assert.Equal("R.reply1", evt2.ObjectName);
        Assert.True(evt2.Targeted);

        tree.Dispose();
    }

    [Fact]
    public void TargetedEvent_CleanedUpOnRemove()
    {
        // Un-drained targeted events don't leak when the waitpoint is removed (Close).
        var tree = new WaitpointTree("R");

        // Post targeted events that will never be consumed
        tree.PostTargeted("R.orphan", new BusEvent("R.orphan", "Reply", ZValue.EmptyNumeric, true));
        tree.PostTargeted("R.orphan", new BusEvent("R.orphan", "Reply", ZValue.EmptyNumeric, true));

        // Remove (simulates Close) — should not throw, events are discarded
        tree.Remove("R.orphan");

        // Subsequent wait returns null (waitpoint gone, recreated empty)
        var evt = tree.Wait("R.orphan", 10);
        Assert.Null(evt);

        tree.Dispose();
    }

    [Fact]
    public void MixedTargetedAndGeneral_LeafWaitSeesBoth()
    {
        // A leaf waiter should see both targeted and general events on their channel.
        var tree = new WaitpointTree("R");

        tree.PostTargeted("R.leaf", new BusEvent("R.leaf", "Reply", ZValue.EmptyNumeric, true));
        tree.PostGeneral("R.leaf", new BusEvent("R.leaf", "Data", ZValue.EmptyNumeric, false));

        // Leaf wait gets targeted first (higher priority), then general
        var evt1 = tree.Wait("R.leaf", 50);
        Assert.NotNull(evt1);
        Assert.Equal("Reply", evt1.EventType);

        var evt2 = tree.Wait("R.leaf", 50);
        Assert.NotNull(evt2);
        Assert.Equal("Data", evt2.EventType);

        tree.Dispose();
    }

    [Fact]
    public void BlastAndDrain_10K_NoLoss()
    {
        // Mirrors the APL blast+drain pattern: post 10K rapidly, then drain all.
        // No active waiter during the blast phase.
        var tree = new WaitpointTree("R");
        const int count = 10000;

        // Establish the waitpoint (simulates warmup)
        tree.PostGeneral("R.sub", new BusEvent("R.sub", "Data", ZValue.EmptyNumeric, false));
        var warmup = tree.Wait("R.sub", 100);
        Assert.NotNull(warmup);

        // Blast — no active waiter
        for (int i = 0; i < count; i++)
        {
            tree.PostGeneral("R.sub", new BusEvent("R.sub", "Data", ZValue.EmptyNumeric, false));
        }

        // Drain
        int drained = 0;
        while (true)
        {
            var evt = tree.Wait("R.sub", 100);
            if (evt == null) break;
            drained++;
        }

        Assert.Equal(count, drained);
        tree.Dispose();
    }
}
