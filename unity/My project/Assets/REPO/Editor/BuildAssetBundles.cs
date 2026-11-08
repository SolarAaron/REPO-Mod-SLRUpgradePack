using UnityEditor;
using System.IO;

public class BuildAssetBundles
{
	[MenuItem("Assets/Build AssetBundles")]
	static void BuildAllAssetBundles() {
		// Ensure the AssetBundles directory exists, and if it doesn't, create it.
		string assetBundleDirectory = "Assets/AssetBundles";
		if (!Directory.Exists(assetBundleDirectory))
			Directory.CreateDirectory(assetBundleDirectory);

		// Build all AssetBundles and place them in the specified directory.
		BuildPipeline.BuildAssetBundles(assetBundleDirectory,
										BuildAssetBundleOptions.None,
										BuildTarget.StandaloneWindows);
	}
}
