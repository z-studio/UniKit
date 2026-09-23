using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZStudio.UniKit.UI {
    /// <summary>负责片段测量、水平布局与视图所有权；不决定条目顺序、次数或播放进度。</summary>
    internal sealed class MarqueeContentLayout : IDisposable {
        internal sealed class Unit {
            public MarqueeItemData Item;
            public int SourceIndex;
            public RectTransform Root;
            public MarqueeClickRelay Relay;
            public long Occurrence;
            internal readonly List<SegmentView> Views = new();
        }

        internal struct SegmentView {
            public RectTransform View;
            public IMarqueeSegmentRenderer Renderer;
            public Vector2 Size;
        }

        private readonly Marquee m_Owner;
        private readonly RectTransform m_Viewport;
        private readonly RectTransform m_Pool;
        private readonly RectTransform m_Track;
        private readonly List<IMarqueeSegmentRenderer> m_Renderers;
        private readonly Dictionary<string, Stack<RectTransform>> m_Pools = new();
        private readonly Stack<Unit> m_Units = new();
        private readonly List<Unit> m_AllUnits = new();
        private bool m_IgnoreTimeScale;
        public Unit SequentialUnit { get; }

        public MarqueeContentLayout(Marquee owner, RectTransform viewport, List<IMarqueeSegmentRenderer> renderers) {
            m_Owner = owner;
            m_Viewport = viewport;
            m_Renderers = renderers;
            m_Pool = CreateRoot("MarqueePool", viewport);
            m_Pool.gameObject.SetActive(false);
            m_Track = CreateRoot("MarqueeTrack", viewport);
            SequentialUnit = CreateUnit("MarqueeContent", viewport);
            SequentialUnit.Root.gameObject.SetActive(false);
        }

        public Unit AcquireUnit() {
            Unit unit = m_Units.Count > 0 ? m_Units.Pop() : CreateUnit("MarqueeUnit", m_Track);
            unit.Root.SetParent(m_Track, false);
            unit.Root.gameObject.SetActive(true);
            return unit;
        }

        public void ReleaseUnit(Unit unit) {
            ClearUnit(unit);
            unit.Root.gameObject.SetActive(false);
            unit.Root.SetParent(m_Pool, false);
            m_Units.Push(unit);
        }

        public Vector2 Measure(MarqueeItemData item, float spacing, bool ignoreTimeScale) {
            if (item?.Segments == null) {
                return Vector2.zero;
            }

            float width = 0f, height = 0f;
            int count = 0;
            var context = new MarqueeRenderContext(m_Viewport, true, ignoreTimeScale);

            foreach (var segment in item.Segments) {
                if (segment == null) {
                    continue;
                }

                var renderer = FindRenderer(segment);

                if (renderer == null) {
                    continue;
                }

                Vector2 size;

                if (renderer is IMarqueeSegmentMeasurer measurer) {
                    size = ValidateSize(measurer.Measure(segment));
                } else {
                    var view = AcquireView(renderer);
                    bool succeeded = false;

                    try {
                        size = ValidateSize(BindView(renderer, view, segment, context));
                        succeeded = true;
                    } finally {
                        // 绑定失败的视图状态未知，销毁；正常测量视图归还共享池。
                        if (succeeded) {
                            Recycle(new SegmentView { View = view, Renderer = renderer });
                        } else {
                            Discard(renderer, view);
                        }
                    }
                }

                if (count++ > 0) {
                    width += Mathf.Max(0f, spacing);
                }

                width += size.x;
                height = Mathf.Max(height, size.y);
            }

            return new Vector2(width, height);
        }

        public Vector2 Bind(Unit unit, MarqueeItemData item, int index, float spacing, bool ignoreTimeScale) {
            ClearUnit(unit);

            if (item?.Segments == null) {
                return Vector2.zero;
            }

            var context = new MarqueeRenderContext(m_Viewport, false, ignoreTimeScale);
            float width = 0f, height = 0f;

            try {
                foreach (var segment in item.Segments) {
                    if (segment == null) {
                        continue;
                    }

                    var renderer = FindRenderer(segment);

                    if (renderer == null) {
                        Debug.LogWarning($"Marquee: 找不到片段 {segment.GetType().Name} 的渲染器。", m_Owner);
                        continue;
                    }

                    var view = AcquireView(renderer);

                    // 在调用外部代码前登记所有权，失败时不会留下游离节点。
                    var entry = new SegmentView { View = view, Renderer = renderer };
                    unit.Views.Add(entry);
                    view.SetParent(unit.Root, false);
                    AlignCenter(view);
                    Vector2 size = ValidateSize(BindView(renderer, view, segment, context));
                    view.sizeDelta = size;
                    entry.Size = size;
                    unit.Views[^1] = entry;

                    if (unit.Views.Count > 1) {
                        width += Mathf.Max(0f, spacing);
                    }

                    width += size.x;
                    height = Mathf.Max(height, size.y);
                }
            } catch {
                // 半绑定视图不能再进入池，否则下一条内容可能继承错误状态。
                foreach (var entry in unit.Views) {
                    Discard(entry.Renderer, entry.View);
                }

                unit.Views.Clear();
                throw;
            }

            float cursor = -width * 0.5f;

            foreach (var entry in unit.Views) {
                entry.View.anchoredPosition = new Vector2(cursor + entry.Size.x * 0.5f, 0f);
                cursor += entry.Size.x + Mathf.Max(0f, spacing);
                entry.View.gameObject.SetActive(true);
            }

            unit.Item = item;
            unit.SourceIndex = index;
            unit.Root.sizeDelta = new Vector2(width, height);
            unit.Relay.Bind(m_Owner, item, index);
            return unit.Root.sizeDelta;
        }

        public void ClearUnit(Unit unit) {
            if (unit == null) {
                return;
            }

            foreach (var entry in unit.Views) {
                Recycle(entry);
            }

            unit.Views.Clear();
            unit.Item = null;

            if (unit.Relay != null) {
                unit.Relay.Bind(null, null, -1);
            }

            if (unit.Root != null) {
                unit.Root.sizeDelta = Vector2.zero;
            }
        }

        // Pause 只影响滚动；这里只传递时间尺度策略，不更改动画的暂停状态。
        public void SetTimeMode(bool ignoreTimeScale) {
            if (m_IgnoreTimeScale == ignoreTimeScale) {
                return;
            }

            m_IgnoreTimeScale = ignoreTimeScale;

            foreach (var unit in m_AllUnits) {
                foreach (var entry in unit.Views) {
                    if (entry.Renderer is IMarqueeSegmentTimeControl control) {
                        control.SetTimeMode(entry.View, ignoreTimeScale);
                    }
                }
            }
        }

        public void Dispose() {
            foreach (var unit in m_AllUnits) {
                ClearUnit(unit);
            }

            if (SequentialUnit.Root != null) {
                Object.Destroy(SequentialUnit.Root.gameObject);
            }

            if (m_Track != null) {
                Object.Destroy(m_Track.gameObject);
            }

            if (m_Pool != null) {
                Object.Destroy(m_Pool.gameObject);
            }

            m_AllUnits.Clear();
            m_Units.Clear();
            m_Pools.Clear();
        }

        private static Vector2 BindView(IMarqueeSegmentRenderer renderer, RectTransform view, MarqueeSegment segment,
            MarqueeRenderContext context) {
            if (renderer is IMarqueeSegmentTimeControl control) {
                control.SetTimeMode(view, context.IgnoreTimeScale);
            }

            return renderer.Bind(view, segment, context);
        }

        private IMarqueeSegmentRenderer FindRenderer(MarqueeSegment segment) {
            foreach (var renderer in m_Renderers) {
                if (renderer.CanRender(segment)) {
                    return renderer;
                }
            }

            foreach (var renderer in MarqueeSegmentRendererRegistry.Renderers) {
                if (renderer.CanRender(segment)) {
                    return renderer;
                }
            }

            return null;
        }

        private RectTransform AcquireView(IMarqueeSegmentRenderer renderer) {
            var pool = GetPool(renderer.Key);
            RectTransform view = null;

            while (pool.Count > 0 && view == null) {
                view = pool.Pop();
            }

            if (view == null) {
                view = renderer.CreateView(m_Pool);
            }

            if (view == null) {
                throw new InvalidOperationException($"渲染器 {renderer.Key} 返回了空视图。");
            }

            view.gameObject.SetActive(false);
            AlignCenter(view);
            return view;
        }

        private void Recycle(SegmentView entry) {
            if (entry.View == null) {
                return;
            }

            try {
                entry.Renderer.OnRecycle(entry.View);
            } catch (Exception exception) {
                // 清理一个扩展失败不能阻断其它片段的回收，也不能污染对象池。
                Debug.LogException(exception, m_Owner);
                entry.View.gameObject.SetActive(false);
                Object.Destroy(entry.View.gameObject);
                return;
            }

            entry.View.gameObject.SetActive(false);
            entry.View.SetParent(m_Pool, false);
            GetPool(entry.Renderer.Key).Push(entry.View);
        }

        private void Discard(IMarqueeSegmentRenderer renderer, RectTransform view) {
            if (view == null) {
                return;
            }

            try {
                renderer.OnRecycle(view);
            } catch (Exception exception) {
                Debug.LogException(exception, m_Owner);
            } finally {
                view.gameObject.SetActive(false);
                Object.Destroy(view.gameObject);
            }
        }

        private Stack<RectTransform> GetPool(string key) {
            if (!m_Pools.TryGetValue(key, out var pool)) {
                pool = new Stack<RectTransform>();
                m_Pools.Add(key, pool);
            }

            return pool;
        }

        private Unit CreateUnit(string name, Transform parent) {
            var root = CreateRoot(name, parent);
            var unit = new Unit { Root = root, Relay = root.gameObject.AddComponent<MarqueeClickRelay>() };
            m_AllUnits.Add(unit);
            return unit;
        }

        private static RectTransform CreateRoot(string name, Transform parent) {
            var root = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            root.SetParent(parent, false);
            AlignCenter(root);
            root.sizeDelta = Vector2.zero;
            return root;
        }

        private static void AlignCenter(RectTransform view) {
            view.anchorMin = view.anchorMax = view.pivot = new Vector2(0.5f, 0.5f);
            view.anchoredPosition = Vector2.zero;
        }

        private static Vector2 ValidateSize(Vector2 size) {
            if (float.IsNaN(size.x) || float.IsNaN(size.y) || float.IsInfinity(size.x) || float.IsInfinity(size.y)) {
                throw new InvalidOperationException("片段渲染器返回了非有限尺寸。");
            }

            return new Vector2(Mathf.Max(0f, size.x), Mathf.Max(0f, size.y));
        }
    }
}