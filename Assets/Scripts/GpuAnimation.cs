using UnityEngine;
using System.Collections.Generic;
public class GpuAnimation:MonoBehaviour{
	
	public GpuAnimationData data;
	public string clipName;
	public float time;
	
	private int boneCount;
	private Dictionary<string,int> boneMap;
	
	private Dictionary<string,GpuAnimationClip> clipMap;
	public GpuAnimationClip GetClip(string clipName){
		return clipMap.ContainsKey(clipName)?clipMap[clipName]:null;
	}
	
	private GpuAnimationClip clip;
	private int frame;
	private int propertyStartPixelIndexID;
	private MaterialPropertyBlock propertyBlock;
	
	public void AddWidget(Transform transform,string boneName){
		if(transform!=null&&boneMap.ContainsKey(boneName)){
			Transform boneTransform=transform.Find(boneName);
			if(boneTransform==null){
				boneTransform=new GameObject(boneName).transform;
				boneTransform.SetParent(transform,false);
			}
			transform.SetParent(boneTransform,false);
		}
	}
	
	public Matrix4x4 GetMatrix(string boneName){
		if(clip!=null&&boneMap.ContainsKey(boneName)){
			return transform.localToWorldMatrix*clip.bones[boneMap[boneName]].frames[frame];
		}
		return Matrix4x4.identity;
	}
	
	public void Play(string name){
		clipName=name;
		time=0;
		Render(GetClip(name),0);
	}
	
	void Awake(){
		boneMap=new Dictionary<string,int>();
		clipMap=new Dictionary<string,GpuAnimationClip>();
		propertyStartPixelIndexID=Shader.PropertyToID("_StartPixelIndex");
		propertyBlock=new MaterialPropertyBlock();
		if(data!=null){
			boneCount=data.bones.Length;
			for(int i=0;i<boneCount;i++){
				boneMap.Add(data.bones[i],i);
			}
			foreach(GpuAnimationClip clip in data.clips){
				clipMap.Add(clip.name,clip);
			}
		}
	}
	
	void Render(GpuAnimationClip clip,int frame){
		if(this.clip!=clip||this.frame!=frame){
			this.clip=clip;
			this.frame=frame;
			if(clip!=null){
				int pixelStartIndex=clip.pixelStartIndex+boneCount*3*frame;
				int childCount=transform.childCount;
				for(int i=0;i<childCount;i++){
					Transform child=transform.GetChild(i);
					MeshRenderer meshRenderer=child.GetComponent<MeshRenderer>();
					if(meshRenderer==null){
						string boneName=child.name;
						int widgetCount=child.childCount;
						if(widgetCount>0&&boneMap.ContainsKey(boneName)){
							Matrix4x4 matrix=clip.bones[boneMap[boneName]].frames[frame];
							Vector3 forward=new Vector3(matrix.m02,matrix.m12,matrix.m22);
							Vector3 upwards=new Vector3(matrix.m01,matrix.m11,matrix.m21);
							child.transform.localRotation=Quaternion.LookRotation(forward,upwards);
							child.transform.localPosition=matrix.GetColumn(3);
						}
					}
					else{
						propertyBlock.SetFloat(propertyStartPixelIndexID,pixelStartIndex);
						meshRenderer.SetPropertyBlock(propertyBlock);
					}
				}
			}
		}
	}
	
	void Update(){
		time+=Time.deltaTime;
		GpuAnimationClip clip=GetClip(clipName);
		if(clip!=null){
			int frame=(int)(time*clip.frameRate);
			if(frame>=clip.frameCount){
				frame=((frame-clip.frameCount)%(clip.frameCount-clip.loopStartFrame))+clip.loopStartFrame;
			}
			Render(clip,frame);
		}
	}
}
