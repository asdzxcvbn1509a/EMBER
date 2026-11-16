using UnityEngine;
using System.Collections;

[ExecuteInEditMode]
public class DisplayWorldPos : MonoBehaviour 
{
    public Vector3 worldPos;

    void Update() 
	{
        worldPos = this.transform.position;
	}
}