public class DecryptAssetBundle : EditorWindow {

    private string assetURL = "";
    private static WWW request;

    private static bool run = false;
    private static IEnumerator en = null;

    [MenuItem("Bundles/Decrypt Asset Bundle")]
    static void Init () {    
        DecryptAssetBundle window = EditorWindow.GetWindow(typeof(DecryptAssetBundle)) as DecryptAssetBundle;
        window.position = new Rect(Screen.width/2,Screen.height/2, 400,150);
    }
    void OnGUI()
    {
        assetURL = EditorGUILayout.TextField("Asset bundle URL: ", assetURL);

        GUILayout.BeginHorizontal();
        if(GUILayout.Button("Clear"))
        {
            assetURL = "";
        }
        if(GUILayout.Button("Decrypt"))
        {
            run=true;
        }
        if(GUILayout.Button("Abort"))
        {
            run=false;
        }
        GUILayout.EndHorizontal();
    }

    public void Update() {
        if (!run) {
            if (en != null)
                en = null;
            return;
        }
        if (en == null) {
            en = LoadAsset(assetUrl);
        }
        if (! en.MoveNext())
            run=false;
    }

    private IEnumerator LoadAsset(string s)
    {
        Debug.Log("Loading " + s + " ...");
        request = new WWW(s);
        while (! request.isDone)
            // avoid freezing the editor:
            yield return ""; // just wait.
        // I don't like the following line because it will freeze up 
        // your editor.  So I commented it out.  
        // yield return request;
        if(request.error != null)
            Debug.LogError(request.error);
        GameObject g = (GameObject) request.assetBundle.mainAsset as GameObject;
        Instantiate(g);
    }

}