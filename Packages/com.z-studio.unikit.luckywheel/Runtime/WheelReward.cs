using System;
using UnityEngine;

namespace ZStudio.UniKit.LuckyWheel {
    /// <summary>一格的结果映射。表现对象与业务数据仅保存引用，由接入方负责创建、布局和表现。</summary>
    [Serializable]
    public sealed class WheelReward {
        [SerializeField]
        private string m_Id;

        [SerializeField]
        private string m_Name;

        [SerializeField]
        private GameObject m_View;

        [SerializeField]
        private UnityEngine.Object m_Data;

        public string Id => m_Id;
        public string Name => string.IsNullOrWhiteSpace(m_Name) ? m_Id : m_Name;

        /// <summary>场景中该格的表现根对象，可包含图片、Spine、特效或任意组合；组件不修改它。</summary>
        public GameObject View => m_View;

        /// <summary>可选业务资产，例如自定义 ScriptableObject。组件不解释其内容。</summary>
        public UnityEngine.Object Data => m_Data;

        /// <summary>运行时业务数据，可传任意 C# 对象，不参与 Unity 序列化。</summary>
        public object UserData { get; }

        public WheelReward(string id, string name = null, GameObject view = null,
            UnityEngine.Object data = null, object userData = null) {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Reward id must not be empty.", nameof(id));
            m_Id = id;
            m_Name = name;
            m_View = view;
            m_Data = data;
            UserData = userData;
        }

        // 复制映射，外部对象仍为引用；不复制场景层级或业务数据。
        internal WheelReward Copy() => new(m_Id, m_Name, m_View, m_Data, UserData);
    }
}