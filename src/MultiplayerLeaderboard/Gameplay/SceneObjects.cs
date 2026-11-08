using UnityEngine;

namespace MultiplayerLeaderboard.Gameplay
{
    // actually optimize the scan (active objects only now). EVERYTHING was scanned before... i really wonder why we had studders.. beyond me.
    internal static class SceneObjects
    {
        internal static T[] Find<T>() where T : Object
        {
            return Object.FindObjectsByType<T>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        }
    }
}
