using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ZStudio.UniKit.UI.Tests {
    public sealed class MarqueeRegressionTests {
        private GameObject m_Object;
        private Marquee m_Marquee;
        private GameObject m_Template;

        private static MarqueeItemData Item(int cycles = -1) => new MarqueeItemData(
            new MarqueeImageSegment { Size = new Vector2(20f, 20f) }) { Cycles = cycles };

        [UnitySetUp]
        public IEnumerator SetUp() {
            yield return new EnterPlayMode();
            m_Object = new GameObject("Marquee test", typeof(RectTransform));
            m_Object.SetActive(false);
            var viewport = (RectTransform)m_Object.transform;
            viewport.sizeDelta = new Vector2(100f, 30f);
            m_Template = new GameObject("Template", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            m_Template.transform.SetParent(viewport, false);
            m_Marquee = m_Object.AddComponent<Marquee>();
            m_Marquee.Viewport = viewport;
            m_Marquee.ContentTemplate = (RectTransform)m_Template.transform;
            m_Marquee.PlayOnStart = false;
            m_Marquee.DisplayDurationWhenFit = 100f;
            m_Object.SetActive(true);
        }

        [UnityTearDown]
        public IEnumerator TearDown() {
            Object.Destroy(m_Object);
            yield return null;
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator CompletionCallbackCannotCompleteNewPlayback() {
            m_Marquee.DisplayDurationWhenFit = 0f;
            m_Marquee.SetItems(new List<MarqueeItemData> { Item() }, false);
            Awaitable next = null;
            m_Marquee.OnAllComplete += () => {
                m_Marquee.DisplayDurationWhenFit = 100f;
                next = m_Marquee.PlayOnceAsync(Item());
            };
            Awaitable first = m_Marquee.PlaySequenceOnceAsync();
            for (int frame = 0; frame < 10 && !first.GetAwaiter().IsCompleted; frame++) yield return null;
            Assert.That(first.GetAwaiter().IsCompleted, Is.True);
            first.GetAwaiter().GetResult();
            Assert.That(next, Is.Not.Null);
            Assert.That(next.GetAwaiter().IsCompleted, Is.False);
            m_Marquee.Stop();
            Assert.Throws<OperationCanceledException>(() => next.GetAwaiter().GetResult());
        }

        [UnityTest]
        public IEnumerator BackgroundCancellationStopsOnMainThread() {
            using var source = new CancellationTokenSource();
            Awaitable result = m_Marquee.PlayOnceAsync(Item(), source.Token);
            var cancellation = System.Threading.Tasks.Task.Run(() => source.Cancel());
            while (!cancellation.IsCompleted) yield return null;
            yield return null;
            Assert.That(m_Marquee.IsPlaying, Is.False);
            Assert.Throws<OperationCanceledException>(() => result.GetAwaiter().GetResult());
        }

        [UnityTest]
        public IEnumerator EmptyReplacementStopsContinuousPlayback() {
            m_Marquee.ScrollMode = MarqueeScrollMode.Continuous;
            m_Marquee.SetItems(new List<MarqueeItemData> { Item() });
            yield return null;
            m_Marquee.SetItems(new List<MarqueeItemData>());
            Assert.That(m_Marquee.IsPlaying, Is.False);
            var track = m_Object.transform.Find("MarqueeTrack");
            Assert.That(track.childCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator RefreshDoesNotCancelAwaitOrResetCycles() {
            m_Marquee.DisplayDurationWhenFit = 0.05f;
            m_Marquee.SetItems(new List<MarqueeItemData> { Item(1) }, false);
            int started = 0;
            m_Marquee.OnItemStart += (_, _) => started++;
            Awaitable result = m_Marquee.PlaySequenceOnceAsync();
            m_Marquee.Refresh();
            yield return new WaitForSeconds(0.2f);
            Assert.That(result.GetAwaiter().IsCompleted, Is.True);
            result.GetAwaiter().GetResult();
            Assert.That(started, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator RemovingComponentDestroysOwnedViewsAndRestoresTemplate() {
            m_Marquee.SetItems(new List<MarqueeItemData> { Item() });
            Object.Destroy(m_Marquee);
            yield return null;
            yield return null;
            Assert.That(m_Template.activeSelf, Is.True);
            Assert.That(m_Object.transform.childCount, Is.EqualTo(1));
        }
    }

    public sealed class MarqueeMathTests {
        [Test]
        public void InvalidPreviousIndexIsRejected() {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                MarqueeMath.GetNextPlayableIndex(new[] { 1 }, -2, true, out _));
        }

        [Test]
        public void ExhaustedBudgetEndsLoop() {
            Assert.That(MarqueeMath.GetNextPlayableIndex(new[] { 0, 0 }, 1, true, out _), Is.EqualTo(-1));
        }

        [TestCase(MarqueeDirection.Left)]
        [TestCase(MarqueeDirection.Right)]
        [TestCase(MarqueeDirection.Up)]
        [TestCase(MarqueeDirection.Down)]
        public void EndPositionTouchesOutsideEdge(MarqueeDirection direction) {
            var viewport = new Vector2(100f, 50f);
            var content = new Vector2(200f, 80f);
            MarqueeMath.ComputeScrollPositions(direction, viewport, content, 10f, out _, out var end);
            float center = MarqueeMath.IsHorizontal(direction) ? end.x : end.y;
            float expected = (MarqueeMath.AxisSize(direction, viewport) + MarqueeMath.AxisSize(direction, content)) * 0.5f;
            Assert.That(center * MarqueeMath.FlowSign(direction), Is.EqualTo(expected));
        }
    }
}
