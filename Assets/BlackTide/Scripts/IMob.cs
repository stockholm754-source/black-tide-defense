using UnityEngine;

namespace BlackTide
{
    // 杂兵的共同接口（近战残像、远程刺鳐）：技能区域和节点用它，不用管是哪种杂兵
    public interface IMob
    {
        void Pull(Vector3 center, float until);   // 旋流吸
        void Root(float dur);                     // 音锚定身
        void Slow(float until);                   // 风障减速
        void Wake();                              // 节点里待命的敌人苏醒
    }
}
