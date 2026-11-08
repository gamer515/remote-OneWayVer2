using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using LearningDispenserAssetKit;

public static class ExportStandaloneLearningV2
{
    const string Root="Assets/LearningDispenserKit_v2";
    const string Output="output/LearningDispenserKit_v2";
    static Dictionary<Mesh,Mesh> meshes=new Dictionary<Mesh,Mesh>();
    static Dictionary<Material,Material> materials=new Dictionary<Material,Material>();
    static Dictionary<Texture,Texture> textures=new Dictionary<Texture,Texture>();
    static void Remap(GameObject obj)
    {
        foreach(var f in obj.GetComponentsInChildren<MeshFilter>(true))
        {
            var source=f.sharedMesh;if(source==null)continue;
            if(!meshes.TryGetValue(source,out var copy)){copy=UnityEngine.Object.Instantiate(source);copy.name=source.name;AssetDatabase.CreateAsset(copy,Root+"/Meshes/"+source.name+"_"+meshes.Count+".asset");meshes.Add(source,copy);}f.sharedMesh=copy;
        }
        foreach(var r in obj.GetComponentsInChildren<Renderer>(true))
        {
            var assigned=r.sharedMaterials;
            for(int i=0;i<assigned.Length;i++)
            {
                var source=assigned[i];if(source==null)continue;
                if(!materials.TryGetValue(source,out var copy))
                {
                    copy=new Material(source){name=source.name};
                    foreach(string property in copy.GetTexturePropertyNames())
                    {
                        var texture=copy.GetTexture(property);if(texture==null)continue;
                        if(!textures.TryGetValue(texture,out var owned))
                        {
                            string path=AssetDatabase.GetAssetPath(texture);if(string.IsNullOrEmpty(path))continue;
                            string dest=Root+"/Textures/"+textures.Count+"_"+Path.GetFileName(path);AssetDatabase.CopyAsset(path,dest);owned=AssetDatabase.LoadAssetAtPath<Texture>(dest);textures.Add(texture,owned);
                        }
                        copy.SetTexture(property,owned);
                    }
                    AssetDatabase.CreateAsset(copy,Root+"/Materials/"+source.name+"_"+materials.Count+".mat");materials.Add(source,copy);
                }
                assigned[i]=copy;
            }
            r.sharedMaterials=assigned;
        }
    }
    static GameObject Box(string name,Transform parent,Vector3 p,Vector3 size,Material mat)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=p;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=mat;return go;
    }
    public static string Build()
    {
        if(Application.isPlaying)throw new Exception("Edit mode required");
        if(File.Exists(Root+"/Prefabs/LearningDispenser_v2.prefab"))return CompleteDemo();
        foreach(var d in new[]{"Prefabs","Meshes","Materials","Textures","Documentation"})Directory.CreateDirectory(Root+"/"+d);Directory.CreateDirectory(Output);AssetDatabase.Refresh();
        var original=GameObject.Find("LearningDispenser");if(!original)throw new Exception("Scene dispenser required");
        var kit=new GameObject("LearningDispenser_v2");
        try
        {
            var model=UnityEngine.Object.Instantiate(original);model.name="Model";
            if(PrefabUtility.IsPartOfPrefabInstance(model))PrefabUtility.UnpackPrefabInstance(model,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            foreach(var component in model.GetComponentsInChildren<MonoBehaviour>(true))UnityEngine.Object.DestroyImmediate(component);
            foreach(var tr in model.GetComponentsInChildren<Transform>(true))tr.gameObject.layer=0;
            model.transform.SetParent(kit.transform,false);model.transform.localPosition=Vector3.zero;model.transform.localRotation=Quaternion.identity;
            model.transform.localScale=new Vector3(1,original.transform.localScale.y/original.transform.localScale.x,1);
            Remap(model);
            var controller=kit.AddComponent<LearningDispenserAsset>();
            controller.outlet=model.transform.Find("OutletPoint_1");controller.receiver=model.transform.Find("OutletPoint_3");controller.spawnedBooks=model.transform.Find("LearningJobs");
            string[] names={"Blue","Red","Yellow","Teal"};controller.selectionButtons=new Collider[4];controller.bookPrefabs=new GameObject[4];
            for(int i=0;i<4;i++)
            {
                controller.selectionButtons[i]=model.transform.Find("03_Column_"+names[i]+"/PressButton").GetComponent<Collider>();
                var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AiAsset/LearningDispenserPrototype/Prefabs/LearningBook_"+i+".prefab");
                var book=UnityEngine.Object.Instantiate(source);
                if(PrefabUtility.IsPartOfPrefabInstance(book))PrefabUtility.UnpackPrefabInstance(book,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
                Remap(book);controller.bookPrefabs[i]=PrefabUtility.SaveAsPrefabAsset(book,Root+"/Prefabs/Book_"+names[i]+".prefab");UnityEngine.Object.DestroyImmediate(book);
            }
            PrefabUtility.SaveAsPrefabAsset(kit,Root+"/Prefabs/LearningDispenser_v2.prefab");
            var paper=new Material(Shader.Find("Universal Render Pipeline/Lit")){name="BoardIvory",color=new Color(.96f,.95f,.89f)};AssetDatabase.CreateAsset(paper,Root+"/Materials/BoardIvory.mat");
            var demoBoard=new GameObject("WhiteBoard");demoBoard.transform.SetParent(kit.transform,false);
            var surface=Box("PaperSurface",demoBoard.transform,new Vector3(.62f,-2.12f,-1.25f),new Vector3(3.7f,.09f,2.65f),paper);
            var boardMat=materials.Values.First(m=>m.name.StartsWith("HoneyWood"));
            Box("BoardLeft",demoBoard.transform,new Vector3(-1.29f,-2.1f,-1.25f),new Vector3(.12f,.13f,2.9f),boardMat);
            Box("BoardRight",demoBoard.transform,new Vector3(2.53f,-2.1f,-1.25f),new Vector3(.12f,.13f,2.9f),boardMat);
            Box("BoardFront",demoBoard.transform,new Vector3(.62f,-2.1f,-2.64f),new Vector3(3.94f,.13f,.12f),boardMat);
            controller.boardSurface=surface.GetComponent<BoxCollider>();
            Remap(demoBoard);
            PrefabUtility.SaveAsPrefabAsset(kit,Root+"/Prefabs/LearningDispenser_WithWhiteBoard.prefab");
            SaveDemo();
        }
        finally{UnityEngine.Object.DestroyImmediate(kit);}
        return FinalizePackage();
    }
    public static string CompleteDemo()
    {
        var kit=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Prefabs/LearningDispenser_v2.prefab"));
        try
        {
            if(PrefabUtility.IsPartOfPrefabInstance(kit))PrefabUtility.UnpackPrefabInstance(kit,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            kit.name="LearningDispenser_WithWhiteBoard";
            var controller=kit.GetComponent<LearningDispenserAsset>();
            var paper=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/BoardIvory.mat");
            var wood=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/HoneyWood_0.mat");
            var board=new GameObject("WhiteBoard");board.transform.SetParent(kit.transform,false);
            var surface=Box("PaperSurface",board.transform,new Vector3(.62f,-2.12f,-1.25f),new Vector3(3.7f,.09f,2.65f),paper);
            Box("BoardLeft",board.transform,new Vector3(-1.29f,-2.1f,-1.25f),new Vector3(.12f,.13f,2.9f),wood);
            Box("BoardRight",board.transform,new Vector3(2.53f,-2.1f,-1.25f),new Vector3(.12f,.13f,2.9f),wood);
            Box("BoardFront",board.transform,new Vector3(.62f,-2.1f,-2.64f),new Vector3(3.94f,.13f,.12f),wood);
            controller.boardSurface=surface.GetComponent<BoxCollider>();
            Remap(board);PrefabUtility.SaveAsPrefabAsset(kit,Root+"/Prefabs/LearningDispenser_WithWhiteBoard.prefab");SaveDemo();
        }
        finally{UnityEngine.Object.DestroyImmediate(kit);}
        return FinalizePackage();
    }
    public static string AddCoinSlots()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Edit mode required.");
        const string destination = "output/LearningDispenserKit_v3";
        Directory.CreateDirectory(destination);

        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(Root + "/Meshes/BeveledBlock_0.asset");
        var brass = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Brass_2.mat");
        var lining = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Recess_3.mat");
        if (!mesh || !brass || !lining) throw new InvalidOperationException("Missing cabinet mesh or materials.");

        var slot = new GameObject("CoinSlot");
        GameObject prefab;
        try
        {
            void Part(string name, Vector3 position, Vector3 size, Material material)
            {
                var piece = new GameObject(name);
                piece.transform.SetParent(slot.transform, false);
                piece.transform.localPosition = position;
                piece.transform.localScale = size;
                piece.AddComponent<MeshFilter>().sharedMesh = mesh;
                piece.AddComponent<MeshRenderer>().sharedMaterial = material;
            }

            // Four independent rails surround a 0.44 × 0.088 opening. The dark lining sits behind the lip.
            Part("BrassTop", new Vector3(0, .113f, -.035f), new Vector3(.72f, .138f, .18f), brass);
            Part("BrassBottom", new Vector3(0, -.113f, -.035f), new Vector3(.72f, .138f, .18f), brass);
            Part("BrassLeft", new Vector3(-.29f, 0, -.035f), new Vector3(.14f, .10f, .18f), brass);
            Part("BrassRight", new Vector3(.29f, 0, -.035f), new Vector3(.14f, .10f, .18f), brass);
            Part("RecessBack", new Vector3(0, 0, .049f), new Vector3(.46f, .10f, .022f), lining);
            prefab = PrefabUtility.SaveAsPrefabAsset(slot, Root + "/Prefabs/CoinSlot.prefab");
        }
        finally { UnityEngine.Object.DestroyImmediate(slot); }

        var cabinetPaths = new[]
        {
            Root + "/Prefabs/LearningDispenser_v2.prefab",
            Root + "/Prefabs/LearningDispenser_WithWhiteBoard.prefab"
        };
        var colors = new[] { "Blue", "Red", "Yellow", "Teal" };
        foreach (string path in cabinetPaths)
        {
            var cabinet = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (string color in colors)
                {
                    var column = cabinet.transform.Find("Model/03_Column_" + color);
                    if (!column || !column.Find("PressButton")) throw new InvalidOperationException("Missing column: " + color);
                    var existing = column.Find("CoinSlot");
                    var installed = existing ? existing : ((GameObject)PrefabUtility.InstantiatePrefab(prefab, column)).transform;
                    installed.localPosition = new Vector3(column.Find("PressButton").localPosition.x, .40f, -.125f);
                }
                PrefabUtility.SaveAsPrefabAsset(cabinet, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(cabinet); }
        }

        foreach (string path in cabinetPaths)
        {
            var cabinet = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            foreach (string color in colors)
            {
                var column = cabinet.transform.Find("Model/03_Column_" + color);
                var installed = column.Find("CoinSlot");
                if (!installed || installed.childCount != 5 ||
                    Mathf.Abs(installed.localPosition.x - column.Find("PressButton").localPosition.x) > .001f)
                    throw new InvalidOperationException("Coin slot validation failed: " + path + " / " + color);
            }
        }

        SaveDemo(destination);
        return ExportPackage(destination, "v3");
    }

    public static string AddBookRackAndCalendar()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Edit mode required.");
        const string destination = "output/LearningDispenserKit_v4";
        Directory.CreateDirectory(destination);
        var block = AssetDatabase.LoadAssetAtPath<Mesh>(Root + "/Meshes/BeveledBlock_0.asset");
        var wood = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/HoneyWood_0.mat");
        var brass = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Brass_2.mat");
        foreach (string name in new[] { "LearningDispenser_v2", "LearningDispenser_WithWhiteBoard" })
        {
            string path = Root + "/Prefabs/" + name + ".prefab";
            var cabinet = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var model = cabinet.transform.Find("Model");
                var dispenser = model.Find("04_LeftDispenser");
                foreach (var piece in dispenser.Cast<Transform>().ToArray())
                {
                    if (piece.name.StartsWith("Receiver") || piece.name.StartsWith("CollectedTile_") ||
                        piece.name == "SlopingChute" || piece.name == "EmergingTile" || piece.name == "FallingTile" ||
                        (piece.name == "OutletLip" && piece.localPosition.y < -.3f))
                        UnityEngine.Object.DestroyImmediate(piece.gameObject);
                }
                var backing = dispenser.Find("Backing");
                backing.localPosition = new Vector3(-2, -.34f, .18f);
                backing.localScale = new Vector3(.95f, .68f, .25f);

                var existing = model.Find("ConnectedBookRack");
                if (existing) UnityEngine.Object.DestroyImmediate(existing.gameObject);
                var rack = new GameObject("ConnectedBookRack").transform;
                rack.SetParent(model, false);
                // One board supports both the left outlet and the right row of books.
                Part(rack, "SharedFloor", block, wood, new Vector3(.03f,-.675f,-.34f), new Vector3(5.06f,.14f,.68f));
                Part(rack, "RaisedReceiverFace", block, brass, new Vector3(-2,-.675f,-.693f), new Vector3(.89f,.14f,.056f));
                Part(rack, "RightFrontRail", block, wood, new Vector3(.50f,-.675f,-.693f), new Vector3(4.10f,.14f,.056f));
                Part(rack, "RightStop", block, wood, new Vector3(2.51f,-.49f,-.34f), new Vector3(.10f,.24f,.68f));
                Part(rack, "BackRail", block, wood, new Vector3(.52f,-.53f,-.02f), new Vector3(4.10f,.16f,.085f));
                var target = rack.gameObject.AddComponent<BoxCollider>();
                target.center = new Vector3(.48f,-.38f,-.38f);
                target.size = new Vector3(3.98f,.45f,.68f);
                target.isTrigger = true;

                var controller = cabinet.GetComponent<LearningDispenserAsset>();
                controller.rackSurface = target;
                controller.receiver.localPosition = new Vector3(-2,-.4375f,-.38f);
                controller.outlet.localPosition = new Vector3(-2,-.22f,-.36f);
                controller.rackSlots = new Transform[4];
                for (int i = 0; i < controller.rackSlots.Length; i++)
                {
                    var marker = new GameObject("BookPosition_" + (i+1)).transform;
                    marker.SetParent(rack, false);
                    marker.localPosition = new Vector3(-1.05f + 1.02f*i,-.4375f,-.38f);
                    controller.rackSlots[i] = marker;
                }
                PrefabUtility.SaveAsPrefabAsset(cabinet, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(cabinet); }
        }
        BuildCalendar(block, wood, brass);
        SaveDemo(destination, true);
        return ExportPackage(destination, "v4");
    }

    static Transform Part(Transform parent, string name, Mesh mesh, Material material, Vector3 position, Vector3 size)
    {
        var piece = new GameObject(name);
        piece.transform.SetParent(parent, false);
        piece.transform.localPosition = position;
        piece.transform.localScale = size;
        piece.AddComponent<MeshFilter>().sharedMesh = mesh;
        piece.AddComponent<MeshRenderer>().sharedMaterial = material;
        return piece.transform;
    }

    static void BuildCalendar(Mesh block, Material wood, Material brass)
    {
        var ivory = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/BoardIvory.mat");
        var midnight = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/CalendarMidnight.mat");
        if (!midnight)
        {
            midnight = new Material(ivory) { color = new Color(.045f,.10f,.23f) };
            AssetDatabase.CreateAsset(midnight, Root + "/Materials/CalendarMidnight.mat");
        }
        var crescent = AssetDatabase.LoadAssetAtPath<Mesh>(Root + "/Meshes/CalendarCrescent.asset");
        if (!crescent)
        {
            const int steps = 24;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int i = 0; i <= steps; i++)
            {
                float angle = Mathf.Lerp(-Mathf.PI/2, Mathf.PI/2, (float)i/steps);
                vertices.Add(new Vector3(-.5f*Mathf.Cos(angle),.5f*Mathf.Sin(angle),0));
                vertices.Add(new Vector3(-.05f*Mathf.Cos(angle),.5f*Mathf.Sin(angle),0));
                if (i > 0)
                {
                    int j = 2*i-2;
                    triangles.AddRange(new[] { j,j+2,j+3,j,j+3,j+1 });
                }
            }
            crescent = new Mesh { name = "CalendarCrescent" };
            crescent.SetVertices(vertices); crescent.SetTriangles(triangles,0); crescent.RecalculateNormals();
            AssetDatabase.CreateAsset(crescent, Root + "/Meshes/CalendarCrescent.asset");
        }

        var calendar = new GameObject("DeskCalendar");
        try
        {
            var root = calendar.transform;
            Part(root,"Foot",block,wood,new Vector3(0,.055f,0),new Vector3(1.71f,.11f,.66f));
            Part(root,"RearStand",block,wood,new Vector3(0,.39f,.14f),new Vector3(1.56f,.78f,.09f)).localRotation = Quaternion.Euler(-22,0,0);
            var face = new GameObject("CalendarFace").transform;
            face.SetParent(root,false); face.localPosition = new Vector3(0,.49f,-.17f); face.localRotation = Quaternion.Euler(12,0,0);
            Part(face,"WoodFrame",block,wood,Vector3.zero,new Vector3(1.68f,.86f,.095f));
            Part(face,"Paper",block,ivory,new Vector3(0,0,-.055f),new Vector3(1.53f,.71f,.035f));
            foreach (float x in new[] { -.49f,0,.49f })
                Part(face,"BrassBinding",block,brass,new Vector3(x,.406f,-.048f),new Vector3(.075f,.17f,.16f));

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            void Label(string name, string value, Vector3 position, float width)
            {
                var label = new GameObject(name);
                label.transform.SetParent(face,false); label.transform.localPosition = position;
                var text = label.AddComponent<TextMesh>();
                text.font = font; text.text = value; text.fontSize = 64; text.characterSize = .05f;
                text.anchor = TextAnchor.MiddleCenter; text.alignment = TextAlignment.Center; text.fontStyle = FontStyle.Bold;
                text.color = new Color(.18f,.10f,.055f);
                var renderer = label.GetComponent<MeshRenderer>(); renderer.sharedMaterial = font.material;
                float measured = renderer.bounds.size.x;
                if (measured > .001f) text.characterSize *= width/measured;
            }
            Label("Date","1005-01-01",new Vector3(0,.137f,-.080f),1.28f);
            Label("Countdown","D-2",new Vector3(-.12f,-.152f,-.080f),.58f);

            var cylinder = AssetDatabase.LoadAssetAtPath<Mesh>(Root + "/Meshes/Cylinder_1.asset");
            var star = AssetDatabase.LoadAssetAtPath<Mesh>(Root + "/Meshes/EightPointStar_2.asset");
            var medallion = Part(face,"DayNightMedallion",cylinder,midnight,new Vector3(.45f,-.153f,-.085f),new Vector3(.27f,.010f,.27f));
            medallion.localRotation = Quaternion.Euler(90,0,0);
            Part(face,"NightMoon",crescent,brass,new Vector3(.49f,-.153f,-.104f),new Vector3(.19f,.19f,1));
            Part(face,"DaySun",star,brass,new Vector3(.45f,-.153f,-.104f),new Vector3(.20f,.20f,.012f)).gameObject.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(calendar, Root + "/Prefabs/DeskCalendar.prefab");
        }
        finally { UnityEngine.Object.DestroyImmediate(calendar); }
    }

    public static string AddEditableCalendarAndLowerShelf()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Edit mode required.");
        const string destination = "output/LearningDispenserKit_v5";
        Directory.CreateDirectory(destination);
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(Root + "/Meshes/BeveledBlock_0.asset");
        var wood = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/HoneyWood_0.mat");
        string path = Root + "/Prefabs/LearningDispenser_WithWhiteBoard.prefab";
        var cabinet = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var previousBoard = cabinet.transform.Find("WhiteBoard");
            if (!previousBoard) previousBoard = cabinet.transform.Find("LowerBookcase");
            if (!previousBoard) throw new InvalidOperationException("Missing board to replace.");
            UnityEngine.Object.DestroyImmediate(previousBoard.gameObject);
            var shelf = new GameObject("LowerBookcase").transform;
            shelf.SetParent(cabinet.transform, false);
            shelf.localPosition = new Vector3(.62f,-2.12f,-1.25f);
            Part(shelf,"ShelfFloor",mesh,wood,Vector3.zero,new Vector3(3.82f,.09f,.86f));
            Part(shelf,"BackBoard",mesh,wood,new Vector3(0,.205f,.40f),new Vector3(3.82f,.40f,.085f));
            Part(shelf,"FrontRail",mesh,wood,new Vector3(0,.028f,-.43f),new Vector3(3.82f,.12f,.075f));
            for (int i = 0; i <= 5; i++)
                Part(shelf,"Divider_" + i,mesh,wood,new Vector3(-1.85f+.74f*i,.185f,0),new Vector3(.05f,.32f,.81f));

            var dropZone = shelf.gameObject.AddComponent<BoxCollider>();
            dropZone.center = new Vector3(0,.22f,0);
            dropZone.size = new Vector3(3.7f,.38f,.80f);
            dropZone.isTrigger = true;
            var controller = cabinet.GetComponent<LearningDispenserAsset>();
            controller.boardSurface = dropZone;
            controller.boardSlots = new Transform[5];
            for (int i = 0; i < controller.boardSlots.Length; i++)
            {
                var marker = new GameObject("BookPosition_" + (i+1)).transform;
                marker.SetParent(shelf,false);
                marker.localPosition = new Vector3(-1.48f+.74f*i,.2125f,0);
                controller.boardSlots[i] = marker;
            }
            PrefabUtility.SaveAsPrefabAsset(cabinet,path);
        }
        finally { PrefabUtility.UnloadPrefabContents(cabinet); }

        string calendarPath = Root + "/Prefabs/DeskCalendar.prefab";
        var calendar = PrefabUtility.LoadPrefabContents(calendarPath);
        try
        {
            var display = calendar.GetComponent<DeskCalendarDisplay>();
            if (!display) display = calendar.AddComponent<DeskCalendarDisplay>();
            var face = calendar.transform.Find("CalendarFace");
            var serialized = new SerializedObject(display);
            serialized.FindProperty("dateLabel").objectReferenceValue = face.Find("Date").GetComponent<TextMesh>();
            serialized.FindProperty("countdownLabel").objectReferenceValue = face.Find("Countdown").GetComponent<TextMesh>();
            serialized.FindProperty("dayIcon").objectReferenceValue = face.Find("DaySun").gameObject;
            serialized.FindProperty("nightIcon").objectReferenceValue = face.Find("NightMoon").gameObject;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(calendar,calendarPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(calendar); }

        SaveDemo(destination,true);
        return ExportPackage(destination,"v5");
    }

    static void SaveDemo(string output = Output, bool includeCalendar = false)
    {
        var previous=SceneManager.GetActiveScene();var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        try
        {
            var copy=PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Prefabs/LearningDispenser_WithWhiteBoard.prefab"),scene) as GameObject;
            var cameraObject=new GameObject("DemoCamera");SceneManager.MoveGameObjectToScene(cameraObject,scene);var camera=cameraObject.AddComponent<Camera>();cameraObject.tag="MainCamera";camera.transform.position=new Vector3(0,5,-13);camera.transform.LookAt(new Vector3(0,1.55f,0));camera.orthographic=true;camera.orthographicSize=4.5f;camera.backgroundColor=new Color(.13f,.16f,.17f);camera.clearFlags=CameraClearFlags.SolidColor;
            copy.GetComponent<LearningDispenserAsset>().inputCamera=camera;
            foreach(var p in new[]{new Vector3(-3,6,-5),new Vector3(4,3,-3)}){var light=new GameObject("DemoLight");SceneManager.MoveGameObjectToScene(light,scene);light.transform.position=p;light.transform.LookAt(new Vector3(0,2,0));var l=light.AddComponent<Light>();l.type=LightType.Directional;l.intensity=p.x<0?1.5f:.6f;}
            GameObject calendar = null;
            if (includeCalendar)
            {
                calendar = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Prefabs/DeskCalendar.prefab"),scene);
                calendar.transform.position = new Vector3(-2.1f,-2.06f,-1.35f);
            }
            EditorSceneManager.SaveScene(scene,Root+"/LearningDispenser_Demo.unity");
            // Offscreen preview must exclude the user's other open scenes and the authoring clone.
            foreach(var tr in copy.GetComponentsInChildren<Transform>(true))tr.gameObject.layer=30;
            if (calendar)
            {
                foreach (var tr in calendar.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer=30;
                var controller = copy.GetComponent<LearningDispenserAsset>();
                for (int i = 0; i < 4; i++)
                {
                    var sample = UnityEngine.Object.Instantiate(controller.bookPrefabs[i],controller.spawnedBooks).transform;
                    var location = i == 0 ? controller.receiver : controller.rackSlots[i];
                    sample.SetPositionAndRotation(location.position,location.rotation);
                    foreach (var tr in sample.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer=30;
                }
                if (controller.boardSlots != null && controller.boardSlots.Length == 5)
                {
                    for (int i = 0; i < 5; i++)
                    {
                        var sample = UnityEngine.Object.Instantiate(controller.bookPrefabs[i%4],controller.spawnedBooks).transform;
                        sample.SetPositionAndRotation(controller.boardSlots[i].position,controller.boardSlots[i].rotation);
                        foreach (var tr in sample.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer=30;
                    }
                }
            }
            camera.cullingMask=1<<30;
            foreach(var root in scene.GetRootGameObjects())if(root.GetComponent<Light>())root.GetComponent<Light>().cullingMask=1<<30;
            var rt=new RenderTexture(900,1300,24);rt.antiAliasing=4;rt.Create();camera.targetTexture=rt;camera.Render();var prev=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(900,1300,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,900,1300),0,0);image.Apply();RenderTexture.active=prev;File.WriteAllBytes(output+"/Preview.png",image.EncodeToPNG());camera.targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(image);
        }
        finally{EditorSceneManager.CloseScene(scene,true);SceneManager.SetActiveScene(previous);}
    }
    public static string FinalizePackage() => ExportPackage(Output, "v2");

    static string ExportPackage(string output, string version)
    {
        File.WriteAllText(Root+"/Documentation/START_HERE.txt","학습 자판기 v2 - 독립 3D 에셋\n\nUnity 6 / URP용 간단한 메시·프리팹입니다.\n1. unitypackage 전체를 Import합니다.\n2. LearningDispenser_Demo 씬을 열고 Play합니다.\n3. 네 색상 버튼을 누르면 책이 배출구로 내려옵니다.\n4. 나온 책을 누른 채 하얀 보드로 끌어 놓습니다. 다시 집어 옮길 수 있습니다.\n5. 바깥에 놓으면 원래 위치로 돌아옵니다.\n\nPrefabs/LearningDispenser_WithWhiteBoard.prefab: 보드 포함, Camera.main 자동 연결.\nPrefabs/LearningDispenser_v2.prefab: 본체와 왼쪽 배출부만. 컨트롤러 Board Surface에 직접 만든 하얀 보드 BoxCollider를 연결합니다. 수평 보드(XZ 평면)를 사용합니다.\n책 4종은 개별 Prefab으로 포함되어 있습니다. 네 개 하단 동전통은 포함하지 않습니다.\n\n수정: Scene에 놓은 프리팹 루트 Scale과 위치를 맞추세요. Selection Buttons, Outlet, Receiver, Book Prefabs가 분리되어 있습니다. Input Camera를 지정하면 해당 카메라로 집기를 처리합니다.\n\n시간·진행 문구는 없습니다. 기존 DecisionManager, MineLearningStation, 저장 시스템에 의존하지 않습니다. 코인 차감·학습 보상·학습 일정 로직은 포함하지 않는 외형/상호작용 에셋입니다. 기존 게임 로직 연결은 별도입니다. 원본 프로젝트의 씬을 덮어쓰지 않습니다.\n");
        if (version == "v3" || version == "v4" || version == "v5")
        {
            string guide = Root + "/Documentation/START_HERE.txt";
            File.WriteAllText(guide, File.ReadAllText(guide).Replace("자판기 v2", "자판기 " + version) +
                "\n버튼 아래 네 곳에 가로형 황동 코인 투입구가 있습니다. Prefabs/CoinSlot.prefab을 개별 배치할 수 있습니다. 투입구는 외형 에셋입니다.\n");
            if (version == "v4") File.AppendAllText(guide,
                "\n자판기 v4: 왼쪽 배출 받침과 오른쪽 가로 거치대의 높이를 맞췄습니다. 배출된 책을 오른쪽 거치대의 빈 자리 또는 하얀 보드로 끌어 놓으세요.\n" +
                "Prefabs/DeskCalendar.prefab: 탁상 달력. CalendarFace 아래 Date, Countdown의 Text Mesh를 편집합니다. 낮에는 DaySun을 켜고 NightMoon을 끄세요.\n");
            if (version == "v5")
            {
                var updated = File.ReadAllText(guide)
                    .Replace("하얀 보드로 끌어 놓습니다.","위쪽 거치대 또는 아래 갈색 책꽂이의 빈 칸으로 끌어 놓습니다.")
                    .Replace("보드 포함, Camera.main 자동 연결.","5칸 갈색 책꽂이 포함, Camera.main 자동 연결. 이전 패키지와 같은 프리팹 경로입니다.");
                File.WriteAllText(guide,updated +
                    "\nPrefabs/DeskCalendar.prefab: Inspector의 Desk Calendar Display에서 Date, Countdown, Is Night를 수정합니다. Day Icon과 Night Icon에 다른 오브젝트를 연결할 수도 있습니다. 실행 중 코드에서는 SetDisplay(date, countdown, isNight)를 호출합니다.\n" +
                    "아래 선반은 책 다섯 권을 한 줄로 놓을 수 있습니다. 차 있는 칸에 놓으면 책이 이전 위치로 돌아옵니다. 위쪽 배출구 옆 선반은 기존 네 칸입니다.\n");
            }
        }
        AssetDatabase.Refresh();AssetDatabase.SaveAssets();
        var deps=AssetDatabase.GetDependencies(new[]{Root+"/Prefabs/LearningDispenser_WithWhiteBoard.prefab",Root+"/LearningDispenser_Demo.unity"},true);
        var outside=deps.Where(p=>p.StartsWith("Assets/")&&!p.StartsWith(Root+"/")).ToArray();
        if(outside.Length>0)throw new Exception("External dependency: "+string.Join(",",outside));
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Prefabs/LearningDispenser_WithWhiteBoard.prefab");
        var copy=UnityEngine.Object.Instantiate(prefab);var camObject=new GameObject("PackageCheckCamera");
        var checks=new List<string>();
        try
        {
            var controller=copy.GetComponent<LearningDispenserAsset>();var cam=camObject.AddComponent<Camera>();cam.transform.position=new Vector3(0,5,-13);cam.transform.LookAt(new Vector3(0,1.55f,0));cam.orthographic=true;cam.orthographicSize=4.5f;controller.inputCamera=cam;
            var book=controller.Dispense(1);if(book==null)throw new Exception("Dispense failed");checks.Add("Standalone dispense creates a book");
            if(!controller.BeginDrag(cam.WorldToScreenPoint(book.position)))throw new Exception("Pick failed");
            Vector3 target=controller.boardSurface.bounds.center;
            controller.MoveDrag(cam.WorldToScreenPoint(target));if(!controller.Drop(cam.WorldToScreenPoint(target)))throw new Exception("Board drop failed");checks.Add("Book can be picked and placed on white board");
            var placed=book.position;if(!controller.BeginDrag(cam.WorldToScreenPoint(placed)))throw new Exception("Repick failed");if(controller.Drop(new Vector2(-100,-100)))throw new Exception("Outside drop accepted");if(Vector3.Distance(placed,book.position)>.001f)throw new Exception("Return failed");checks.Add("Outside drop restores prior position");
            if(controller.BookCount!=1)throw new Exception("Book duplicated");checks.Add("Movement does not duplicate books");checks.Add("All prefab dependencies stay within package folder");
            if (version == "v4" || version == "v5")
            {
                if (!controller.BeginDrag(cam.WorldToScreenPoint(book.position)) ||
                    !controller.Drop(cam.WorldToScreenPoint(controller.rackSlots[0].position)) ||
                    Vector3.Distance(book.position, controller.rackSlots[0].position) > .001f)
                    throw new Exception("Shelf placement failed");
                var next = controller.Dispense(0);
                if (!next || Vector3.Distance(next.position,controller.receiver.position) > .001f || controller.Dispense(2) != null)
                    throw new Exception("Receiver placement or occupied receiver guard failed");
                if (!controller.BeginDrag(cam.WorldToScreenPoint(next.position)) ||
                    controller.Drop(cam.WorldToScreenPoint(controller.rackSlots[0].position)) ||
                    Vector3.Distance(next.position,controller.receiver.position) > .001f)
                    throw new Exception("Occupied shelf slot guard failed");
                checks.Add("Book snaps to a shelf slot; receiver and occupied slots prevent overlap");
                var calendar = AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Prefabs/DeskCalendar.prefab");
                var face = calendar.transform.Find("CalendarFace");
                if (face.Find("Date").GetComponent<TextMesh>().text != "1005-01-01" ||
                    face.Find("Countdown").GetComponent<TextMesh>().text != "D-2" ||
                    !face.Find("NightMoon").gameObject.activeSelf || face.Find("DaySun").gameObject.activeSelf)
                    throw new Exception("Calendar defaults invalid");
                checks.Add("Calendar defaults to 1005-01-01, D-2 and night icon with switchable sun");
                if (version == "v5")
                {
                    if (controller.boardSlots.Length != 5) throw new Exception("Lower shelf must have five slots");
                    for (int i = 0; i < 5; i++)
                    {
                        var ready = i == 0 ? next : controller.Dispense(i%4);
                        var position = controller.boardSlots[i].position;
                        if (!ready || !controller.BeginDrag(cam.WorldToScreenPoint(ready.position)) ||
                            !controller.Drop(cam.WorldToScreenPoint(position)) || Vector3.Distance(ready.position,position) > .001f)
                            throw new Exception("Lower shelf placement failed for slot " + i);
                    }
                    checks.Add("All five brown shelf slots accept one book each");
                    var calendarCopy = UnityEngine.Object.Instantiate(calendar);
                    try
                    {
                        var display = calendarCopy.GetComponent<DeskCalendarDisplay>();
                        display.SetDisplay("1005-01-02","D-1",false);
                        var displayFace = calendarCopy.transform.Find("CalendarFace");
                        if (displayFace.Find("Date").GetComponent<TextMesh>().text != "1005-01-02" ||
                            displayFace.Find("Countdown").GetComponent<TextMesh>().text != "D-1" ||
                            !displayFace.Find("DaySun").gameObject.activeSelf || displayFace.Find("NightMoon").gameObject.activeSelf)
                            throw new Exception("Editable calendar did not update text and day/night state");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(calendarCopy); }
                    checks.Add("Calendar applies edited date, countdown and day icon");
                }
            }
        }
        finally{UnityEngine.Object.DestroyImmediate(copy);UnityEngine.Object.DestroyImmediate(camObject);}
        if (version == "v3") checks.Add("Four centered coin slots in both cabinet prefabs");
        File.WriteAllText(output+"/checks.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{passed=true,checks,dependencies=deps},Newtonsoft.Json.Formatting.Indented));
        AssetDatabase.ExportPackage(Root,output+"/LearningDispenserKit_"+version+".unitypackage",ExportPackageOptions.Recurse);
        File.Copy(Root+"/Documentation/START_HERE.txt",output+"/START_HERE.txt",true);
        return "Standalone " + version + " exported; " + checks.Count + " checks passed";
    }
}
