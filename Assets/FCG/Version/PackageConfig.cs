using UnityEngine;

namespace FCG
{
    [CreateAssetMenu(fileName = "PackageConfig", menuName = "Package/Config", order = 1)]
    public class PackageConfig : ScriptableObject
    {
        public string packageName;
        public string version;
        public string releaseDate;
        public string description;
        public string unityVersion;
        public string author;
        public string[] dependencies;
    }
}
