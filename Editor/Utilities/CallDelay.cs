using UnityEngine;
using System;

namespace Unity.MemoryProfiler.Editor
{
    internal class CallDelay
    {
        float m_DelayTarget;
        Action m_OnSearchChanged;

        public void Start(Action searchChanged, float delayTime)
        {
            m_DelayTarget = Time.realtimeSinceStartup + delayTime;
            m_OnSearchChanged = searchChanged;
        }

        public void Trigger()
        {
            if (!IsDone || m_OnSearchChanged == null)
                return;

            m_OnSearchChanged.Invoke();
            m_OnSearchChanged = null;
            m_DelayTarget = 0;
        }

        public bool HasTriggered
        {
            get { return m_OnSearchChanged == null; }
        }

        public bool IsDone
        {
            get { return Time.realtimeSinceStartup >= m_DelayTarget; }
        }
    }
}
