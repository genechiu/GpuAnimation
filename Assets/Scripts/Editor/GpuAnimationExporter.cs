using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;

public class GpuAnimationExporter:Editor {
	
    [MenuItem("Assets/Export Animation",false,0)]
	public static void Export(){
		foreach(Object selection in Selection.objects){
			if(selection is DefaultAsset){
				string path=AssetDatabase.GetAssetPath(selection);
				GameObject prefab=LoadPrefabOrFBX(path+"/"+selection.name);
				if(prefab.transform.childCount>0){
					ExportAnimation(prefab);
				}
			}
		}
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
	}

	public static void ExportAnimation(GameObject prefab){
		string prefabPath=AssetDatabase.GetAssetPath(prefab);
		string rawFolderPath=Path.GetDirectoryName(prefabPath);
		string outFolderPath=rawFolderPath.Replace("Res","Resources");
		string materialsFolderPath=outFolderPath+"/Materials";
		if(!Directory.Exists(materialsFolderPath)){
			Directory.CreateDirectory(materialsFolderPath);
		}
		
		GpuAnimationData animationData=ScriptableObject.CreateInstance<GpuAnimationData>();
		GameObject gameObject=GameObject.Instantiate<GameObject>(prefab);
		Transform[] children=gameObject.transform.GetChild(0).GetComponentsInChildren<Transform>();
		Dictionary<string,int> indexMap=new Dictionary<string,int>();
		int boneCount=children.Length;
		Matrix4x4[] bonePoses=new Matrix4x4[boneCount];
		Matrix4x4[] bindPoses=new Matrix4x4[boneCount];
		string[] bones=new string[boneCount];
		animationData.bones=bones;
		for(int i=0;i<boneCount;i++){
			Transform child=children[i];
			indexMap.Add(child.name,i);
			bonePoses[i]=child.transform.localToWorldMatrix;
			bindPoses[i]=child.transform.worldToLocalMatrix;
			bones[i]=child.name;
		}
		HashSet<string> widgetPaths=new HashSet<string>();
		List<AnimationClip> animationClips=new List<AnimationClip>();
		foreach(string rawFile in Directory.GetFiles(rawFolderPath)){
			if(rawFile.IndexOf('@')>=0){
				AnimationClip animationClip=AssetDatabase.LoadAssetAtPath<AnimationClip>(rawFile);
				if(animationClip!=null){
					animationClips.Add(animationClip);
				}
			}
			else{
				if(AssetDatabase.LoadAssetAtPath<MeshRenderer>(rawFile)!=null){
					string path=rawFolderPath+"/"+Path.GetFileNameWithoutExtension(rawFile);
					if(!widgetPaths.Contains(path)){
						widgetPaths.Add(path);
					}
				}
			}
		}
		foreach(string path in widgetPaths){
			GameObject linkPrefab=LoadPrefabOrFBX(path);
			if(linkPrefab!=null){
				GameObject linkGameObject=GameObject.Instantiate<GameObject>(linkPrefab);
				MeshFilter linkMeshFilter=linkGameObject.GetComponent<MeshFilter>();
				MeshRenderer linkMeshRenderer=linkGameObject.GetComponent<MeshRenderer>();
				Material sharedMaterial=linkMeshRenderer.sharedMaterial;
				Material newMaterial=new Material(Shader.Find("Toon/Default"));
				newMaterial.mainTexture=sharedMaterial.mainTexture;
				newMaterial.enableInstancing=true;
				linkMeshRenderer.sharedMaterial=newMaterial;
				Mesh sharedMesh=linkMeshFilter.sharedMesh;
				Mesh newMesh=new Mesh();
				newMesh.vertices=sharedMesh.vertices;
				newMesh.normals=sharedMesh.normals;
				newMesh.uv=sharedMesh.uv;
				newMesh.triangles=sharedMesh.triangles;
				AssetDatabase.CreateAsset(newMesh,materialsFolderPath+"/"+linkPrefab.name+".asset");
				AssetDatabase.CreateAsset(newMaterial,materialsFolderPath+"/"+linkPrefab.name+".mat");
				PrefabUtility.CreatePrefab(outFolderPath+"/"+linkPrefab.name+".prefab",linkGameObject);
				GameObject.DestroyImmediate(linkGameObject);
			}
		}

		int pixelStartIndex=boneCount*3;
		int clipCount=animationClips.Count;
		GpuAnimationClip[] clips=new GpuAnimationClip[clipCount];
		animationData.clips=clips;
		for(int i=0;i<clipCount;i++){
			AnimationClip animationClip=animationClips[i];
			GpuAnimationClip clip=new GpuAnimationClip();
			clips[i]=clip;
			clip.name=animationClip.name;
			clip.frameRate=Mathf.RoundToInt(animationClip.frameRate);
			clip.frameCount=Mathf.RoundToInt(animationClip.length*clip.frameRate);
			clip.loopStartFrame=animationClip.wrapMode==WrapMode.Loop?0:(clip.frameCount-1);
			clip.length=(float)clip.frameCount/clip.frameRate;
			clip.pixelStartIndex=pixelStartIndex;
			pixelStartIndex+=clip.frameCount*boneCount*3;
			GpuAnimationClip.Bone[] clipBones=new GpuAnimationClip.Bone[boneCount];
			clip.bones=clipBones;
			for(int b=0;b<boneCount;b++){
				clipBones[b]=new GpuAnimationClip.Bone();
				clipBones[b].frames=new Matrix4x4[clip.frameCount];
			}
		}
		int textureSize=2;
		while(textureSize*textureSize<pixelStartIndex){
			textureSize=textureSize<<1;
		}
		Texture2D texture=new Texture2D(textureSize,textureSize,TextureFormat.RGBAHalf,false,true);
		texture.filterMode=FilterMode.Point;
		int pixelIndex=0;
		Color[] pixels=texture.GetPixels();
		Matrix4x4 matrix=Matrix4x4.identity;
		for(int b=0;b<boneCount;b++){
			pixels[pixelIndex++]=new Color(matrix.m00,matrix.m01,matrix.m02,matrix.m03);
			pixels[pixelIndex++]=new Color(matrix.m10,matrix.m11,matrix.m12,matrix.m13);
			pixels[pixelIndex++]=new Color(matrix.m20,matrix.m21,matrix.m22,matrix.m23);
		}

		for(int c=0;c<clipCount;c++){
			GpuAnimationClip clip=clips[c];
			AnimationClip animationClip=animationClips[c];
			EditorCurveBinding[] curveBindings=AnimationUtility.GetCurveBindings(animationClip);

			HashSet<string> positionPathHash=new HashSet<string>();
			HashSet<string> rotationPathHash=new HashSet<string>();
			foreach(EditorCurveBinding curveBinding in curveBindings){
				string path=curveBinding.path;
				string propertyName=curveBinding.propertyName;
				if(propertyName.Length==17){
					string propertyPrefix=propertyName.Substring(0,15);
					if(propertyPrefix=="m_LocalPosition"){
						if(!positionPathHash.Contains(path)){
							positionPathHash.Add(path);
						}
					}
					else if(propertyPrefix=="m_LocalRotation"){
						if(!rotationPathHash.Contains(path)){
							rotationPathHash.Add(path);
						}
					}
				}
			}

			for(int f=0;f<clip.frameCount;f++){
				float time=(float)f/clip.frameRate;
				
				foreach(string path in positionPathHash){
					string boneName=path.Substring(path.LastIndexOf('/')+1);
					if(indexMap.ContainsKey(boneName)){
						Transform child=children[indexMap[boneName]];
						float positionX=GetCurveValue(animationClip,path,"m_LocalPosition.x",time);
						float positionY=GetCurveValue(animationClip,path,"m_LocalPosition.y",time);
						float positionZ=GetCurveValue(animationClip,path,"m_LocalPosition.z",time);
						child.localPosition=new Vector3(positionX,positionY,positionZ);
					}
				}
				
				foreach(string path in rotationPathHash){
					string boneName=path.Substring(path.LastIndexOf('/')+1);
					if(indexMap.ContainsKey(boneName)){
						Transform child=children[indexMap[boneName]];
						float rotationX=GetCurveValue(animationClip,path,"m_LocalRotation.x",time);
						float rotationY=GetCurveValue(animationClip,path,"m_LocalRotation.y",time);
						float rotationZ=GetCurveValue(animationClip,path,"m_LocalRotation.z",time);
						float rotationW=GetCurveValue(animationClip,path,"m_LocalRotation.w",time);
						Quaternion rotation=new Quaternion(rotationX,rotationY,rotationZ,rotationW);
						float r=rotation.x*rotation.x;
						r+=rotation.y*rotation.y;
						r+=rotation.z*rotation.z;
						r+=rotation.w*rotation.w;
						if(r>0.1f){
							r=1.0f/Mathf.Sqrt(r);
							rotation.x*=r;
							rotation.y*=r;
							rotation.z*=r;
							rotation.w*=r;
						}
						child.localRotation=rotation;
					}
				}

				for(int b=0;b<boneCount;b++){
					matrix=children[b].transform.localToWorldMatrix;
					clip.bones[b].frames[f]=matrix;
					matrix=matrix*bindPoses[b];
					pixels[pixelIndex++]=new Color(matrix.m00,matrix.m01,matrix.m02,matrix.m03);
					pixels[pixelIndex++]=new Color(matrix.m10,matrix.m11,matrix.m12,matrix.m13);
					pixels[pixelIndex++]=new Color(matrix.m20,matrix.m21,matrix.m22,matrix.m23);
				}
			}
		}
		
		texture.SetPixels(pixels);
		texture.Apply();
		AssetDatabase.CreateAsset(texture,materialsFolderPath+"/"+prefab.name+"_skinning.asset");
		AssetDatabase.CreateAsset(animationData,materialsFolderPath+"/"+prefab.name+"_data.asset");
		
		GpuAnimation animation=new GameObject().AddComponent<GpuAnimation>();
		animation.data=animationData;
		animation.clipName="idle";
		
		Dictionary<Mesh,Mesh> meshMap=new Dictionary<Mesh,Mesh>();
		Dictionary<Material,Material> materialMap=new Dictionary<Material,Material>();
		SkinnedMeshRenderer[] skinnedMeshRenderers=gameObject.GetComponentsInChildren<SkinnedMeshRenderer>();
		for(int p=0;p<skinnedMeshRenderers.Length;p++){
			SkinnedMeshRenderer skinnedMeshRenderer=skinnedMeshRenderers[p];
			Mesh sharedMesh=skinnedMeshRenderer.sharedMesh;
			Mesh newMesh;
			if(meshMap.ContainsKey(sharedMesh)){
				newMesh=meshMap[sharedMesh];
			}
			else{
				Transform[] transforms=skinnedMeshRenderer.bones;
				int vertexCount=sharedMesh.vertexCount;
				List<Vector4> indices=new List<Vector4>();
				List<Vector4> weights=new List<Vector4>();
				Vector3[] vertices=new Vector3[vertexCount];
				Vector3[] normals=new Vector3[vertexCount];
				Matrix4x4 meshMatrix=bonePoses[indexMap[transforms[0].name]]*sharedMesh.bindposes[0];
				BoneWeight[] boneWeights=sharedMesh.boneWeights;
				for(int v=0;v<vertexCount;v++){
					BoneWeight weight=boneWeights[v];
					float weight0=weight.weight0;
					float weight1=weight.weight1;
					float weight2=weight.weight2;
					float weight3=weight.weight3;
					int boneIndex0=indexMap[transforms[weight.boneIndex0].name];
					int boneIndex1=indexMap[transforms[weight.boneIndex1].name];
					int boneIndex2=indexMap[transforms[weight.boneIndex2].name];
					int boneIndex3=indexMap[transforms[weight.boneIndex3].name];
					indices.Add(new Vector4(boneIndex0,boneIndex1,boneIndex2,boneIndex3));
					weights.Add(new Vector4(weight0,weight1,weight2,weight3));
					vertices[v]=meshMatrix*sharedMesh.vertices[v];
					normals[v]=meshMatrix*sharedMesh.normals[v];
					weight.boneIndex0=boneIndex0;
					weight.boneIndex1=boneIndex1;
					weight.boneIndex2=boneIndex2;
					weight.boneIndex3=boneIndex3;
					boneWeights[v]=weight;
				}
				newMesh=new Mesh();
				newMesh.vertices=vertices;
				newMesh.normals=normals;
				newMesh.triangles=sharedMesh.triangles;
				newMesh.uv=sharedMesh.uv;
				newMesh.SetUVs(1,indices);
				newMesh.SetUVs(2,weights);
				skinnedMeshRenderer.bones=children;
				skinnedMeshRenderer.sharedMesh=newMesh;
				AssetDatabase.CreateAsset(newMesh,materialsFolderPath+"/"+sharedMesh.name+".asset");
				meshMap.Add(sharedMesh,newMesh);
			}

			Material sharedMaterial=skinnedMeshRenderer.sharedMaterial;
			Material newMaterial;
			if(materialMap.ContainsKey(sharedMaterial)){
				newMaterial=materialMap[sharedMaterial];
			}
			else{
				newMaterial=new Material(Shader.Find("Toon/Animation"));
				newMaterial.mainTexture=sharedMaterial.mainTexture;
				newMaterial.SetTexture("_SkinningTex",texture);
				newMaterial.SetFloat("_SkinningTexSize",textureSize);
				newMaterial.enableInstancing=true;
				AssetDatabase.CreateAsset(newMaterial,materialsFolderPath+"/"+sharedMaterial.name+".mat");
				materialMap.Add(sharedMaterial,newMaterial);
			}
			
			GameObject partGameObject=new GameObject(skinnedMeshRenderer.name);
			MeshFilter meshFilter=partGameObject.AddComponent<MeshFilter>();
			meshFilter.sharedMesh=newMesh;
			MeshRenderer meshRenderer=partGameObject.AddComponent<MeshRenderer>();
			meshRenderer.sharedMaterial=newMaterial;
			meshRenderer.lightProbeUsage=skinnedMeshRenderer.lightProbeUsage;
			meshRenderer.reflectionProbeUsage=skinnedMeshRenderer.reflectionProbeUsage;
			meshRenderer.shadowCastingMode=skinnedMeshRenderer.shadowCastingMode;
			meshRenderer.receiveShadows=skinnedMeshRenderer.receiveShadows;
			PrefabUtility.CreatePrefab(outFolderPath+"/"+skinnedMeshRenderer.name+".prefab",partGameObject);
			partGameObject.transform.SetParent(animation.transform,false);
		}
		
		PrefabUtility.CreatePrefab(outFolderPath+"/"+prefab.name+".prefab",animation.gameObject);
		GameObject.DestroyImmediate(animation.gameObject);
		GameObject.DestroyImmediate(gameObject);
	}
	
	private static float GetCurveValue(AnimationClip clip,string path,string prop,float time){
		EditorCurveBinding binding=EditorCurveBinding.FloatCurve(path,typeof(Transform),prop);
		return AnimationUtility.GetEditorCurve(clip,binding).Evaluate(time);
	}
	
	public static GameObject LoadPrefabOrFBX(string path){
		GameObject prefab=AssetDatabase.LoadAssetAtPath<GameObject>(string.Format("{0}.prefab",path));
		if(prefab==null){
			prefab=AssetDatabase.LoadAssetAtPath<GameObject>(string.Format("{0}.FBX",path));
		}
		return prefab;
	}

	public static void Print(params object[] messages){
		string[] strings=new string[messages.Length];
		for(int i=0;i<strings.Length;i++){
			strings[i]=messages[i]==null?"null":messages[i].ToString();
		}
		Debug.Log(string.Join("|",strings));
	}
}
