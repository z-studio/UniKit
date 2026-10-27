using System;
using UnityEngine;

namespace ZStudio.UniKit.LuckyWheel {
    /// <summary>
    /// 一格奖励的配置，不执行抽奖或发奖。使用奖励列表索引指定圆盘停在哪一格。
    /// View 是场景美术，Data 是可保存到资产的 Unity 数据，UserData 是仅运行时存在的普通 C# 数据。
    /// 对象与数据均为引用；创建、布局和中奖表现由接入方负责。
    /// </summary>
    [Serializable]
    public sealed class WheelReward {
        [SerializeField]
        private string m_Name;

        [SerializeField]
        private GameObject m_View;

        [SerializeField]
        private UnityEngine.Object m_Data;

        /// <summary>可选名称，方便 Inspector、日志和示例显示，不决定美术文字，也不用于查找奖格。</summary>
        public string Name => m_Name;

        /// <summary>场景中该格的表现根对象，可包含图片、Spine、特效或任意组合；组件不修改它。</summary>
        public GameObject View => m_View;

        /// <summary>可选业务资产，例如自定义 ScriptableObject。组件不解释其内容。</summary>
        public UnityEngine.Object Data => m_Data;

        /// <summary>运行时业务数据，可传任意 C# 对象，不参与 Unity 序列化。</summary>
        public object UserData { get; }

        public WheelReward(string name = null, GameObject view = null,
            UnityEngine.Object data = null, object userData = null) {
            m_Name = name;
            m_View = view;
            m_Data = data;
            UserData = userData;
        }

        // 复制映射，外部对象仍为引用；不复制场景层级或业务数据。
        internal WheelReward Copy() => new(m_Name, m_View, m_Data, UserData);
    }
}
