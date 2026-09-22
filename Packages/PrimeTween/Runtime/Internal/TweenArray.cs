using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace PrimeTween {
    internal unsafe class TweenArray {
        private TweenData[] m_Tweens;
        private NativeArray<UnmanagedTweenData> m_Data;
        internal UnmanagedTweenData* DataPtr { get; private set; }
        private int m_Capacity;
        private int m_NumLocks;
        internal readonly string name;
        internal int Count { get; private set; }

        public TweenArray(int capacity, string name) {
            CreateBuffers(capacity);
            this.name = name;
        }

        internal bool IsLocked => m_NumLocks > 0;

        private void CreateBuffers(int capacity) {
            Assert.IsFalse(IsLocked);
            Assert.IsTrue(capacity >= 0);
            m_Tweens = new TweenData[capacity];
            m_Data = new NativeArray<UnmanagedTweenData>(capacity, Allocator.Persistent);
            DataPtr = (UnmanagedTweenData*)m_Data.GetUnsafePtr();
            m_Capacity = capacity;
        }

        internal ref TweenData this[int index] {
            get {
                Assert.IsTrue(index >= 0 && index < m_Capacity);
                return ref m_Tweens[index];
            }
        }

        internal void MoveAndClearOld(TweenData tween, int oldIndex, int newIndex) {
            Assert.IsTrue(newIndex < oldIndex);
            Assert.IsTrue(oldIndex >= 0 && oldIndex < m_Capacity);
            Assert.IsTrue(newIndex >= 0 && newIndex < m_Capacity);
            Assert.AreEqual(this[oldIndex], tween);

            m_Data[newIndex] = m_Data[oldIndex];
            Assert.IsNotNull(tween.cold);
            tween.cold.index = newIndex;
            this[newIndex] = tween;
            Assert.AreEqual(tween.Id, m_Data[newIndex].id);

            this[oldIndex] = default; // setting to null is important because ProcessAll filters nulls
            m_Data[oldIndex] = default;
        }

        internal void RemoveLast(ColdData cold) {
            Assert.IsFalse(IsLocked);
            Assert.IsTrue(Count > 0);
            Assert.IsNotNull(cold);
            Assert.IsNotNull(cold.tweenArray);
            Assert.AreNotEqual(-1, cold.index);

            int i = Count - 1;
            Assert.AreEqual(this[i].cold, cold);
            cold.tweenArray = null;
            cold.index = -1;
            this[i] = default;
            m_Data[i] = default;
            Count--;
        }

        internal void TrimEndNulls(int numRemoved) {
            Assert.IsFalse(IsLocked);

            for (int i = Count - numRemoved; i < Count; i++) {
                Assert.IsNull(this[i].cold);
                Assert.AreEqual(0, m_Data[i].id);
            }

            Count -= numRemoved;
        }

        internal void Clear() {
            Assert.IsFalse(IsLocked);

            for (int i = 0; i < Count; i++) {
                this[i] = default;
                m_Data[i] = default;
            }

            Count = 0;
        }

        public void Add(ColdData tween) {
            Assert.IsFalse(IsLocked, name);
            Assert.IsNotNull(tween);
            int i = Count;
            Assert.IsTrue(i <= Capacity);

            if (i == Capacity) {
                Assert.AreEqual(i, m_Tweens.Length);
                Assert.AreEqual(i, m_Data.Length);
                Capacity = Capacity == 0 ? 4 : Capacity * 2;
            }

            Assert.AreEqual(0, m_Data[i].id);
            Assert.AreNotEqual(0, tween.id);

            tween.tweenArray = this;
            tween.index = i;
            Assert.IsTrue(tween.Data.IsDefault());

            Assert.AreNotEqual(0, tween.id);
            tween.Data.id = tween.id;
            this[i] = new TweenData { cold = tween };
            Count++;
            Assert.AreEqual(tween.id, tween.Data.id);
        }

        public int Capacity {
            get => m_Capacity;
            set {
                Assert.IsFalse(IsLocked);
                Assert.IsTrue(value >= 0);
                Assert.IsTrue(value >= Count);
                Assert.IsTrue(m_Data.IsCreated);

                if (m_Capacity != value) {
                    // Debug.Log($"set capacity to {value}");
                    TweenData[] oldTweens = m_Tweens;
                    NativeArray<UnmanagedTweenData> oldData = m_Data;

                    CreateBuffers(value);

                    Array.Copy(oldTweens, m_Tweens, Count);
                    NativeArray<UnmanagedTweenData>.Copy(oldData, m_Data, Count);
                    oldData.Dispose();

                    foreach (var el in this) {
                        TweenData tween = el.Tween;
                        UnmanagedTweenData data = el.Data;

                        Assert.IsNotNull(tween.cold);
                        Assert.AreEqual(data.id, tween.cold.Data.id);
                        Assert.AreEqual(tween.Id, m_Data[el.index].id);
                        Assert.AreEqual(tween.Id, tween.cold.id);
                        Assert.AreEqual(el.index, tween.cold.index);
                    }
                }
            }
        }

        public void Dispose() {
            Assert.IsFalse(IsLocked);
            Assert.IsTrue(m_Data.IsCreated);
            m_Data.Dispose();
            m_Data = default;
            m_Capacity = 0;
            Count = 0;
        }

#if TEST_FRAMEWORK_INSTALLED
        internal ColdData Single() {
            Assert.AreEqual(1, Count);
            return this[0].cold;
        }
#endif

        internal NativeArray<UnmanagedTweenData> GetData() => m_Data;

        internal ref UnmanagedTweenData GetDataAt(int index) {
            Assert.IsTrue(IsLocked);
            return ref UnsafeUtility.AsRef<UnmanagedTweenData>(DataPtr + index);
        }

        internal readonly struct Lock : IDisposable {
            private readonly TweenArray m_Array;

            internal Lock(TweenArray array) {
                m_Array = array;
                Assert.IsTrue(array.m_NumLocks >= 0);
                array.m_NumLocks++;
            }

            public void Dispose() {
                m_Array.m_NumLocks--;
                Assert.IsTrue(m_Array.m_NumLocks >= 0);
            }
        }

        public Enumerator GetEnumerator() => new Enumerator(this);

        public struct Enumerator : IDisposable {
            private readonly TweenArray m_Array;
            private int m_Index;
            private readonly Lock m_Lock;

            internal Enumerator(TweenArray array) {
                Assert.IsTrue(array.m_Data.IsCreated || array.m_Capacity == 0);
                m_Array = array;
                m_Index = -1;
                m_Lock = new Lock(array);
            }

            public EnumeratorElement Current => new(m_Array, m_Index);

            public bool MoveNext() {
                m_Index++;
                return m_Index < m_Array.Count;
            }

            void IDisposable.Dispose() => m_Lock.Dispose();
        }

        public struct EnumeratorElement {
            private readonly TweenArray m_Array;
            internal readonly int index;

            internal EnumeratorElement(TweenArray array, int index) {
                m_Array = array;
                this.index = index;
            }

            public ref TweenData Tween => ref m_Array[index];
            public ref UnmanagedTweenData Data => ref m_Array.GetDataAt(index);
        }
    }
}