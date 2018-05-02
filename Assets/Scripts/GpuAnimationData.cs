using UnityEngine;

[System.Serializable]
public class GpuAnimationData:ScriptableObject{
	public string[] bones;
	public GpuAnimationClip[] clips;
}
