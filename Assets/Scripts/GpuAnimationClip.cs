using UnityEngine;

[System.Serializable]
public class GpuAnimationClip{
	public string name;
	public int pixelStartIndex;
	public int loopStartFrame;
	public int frameCount;
	public int frameRate;
	public float length;
	public Bone[] bones;
	
	[System.Serializable]
	public class Bone{
		public Matrix4x4[] frames;
	}
}
