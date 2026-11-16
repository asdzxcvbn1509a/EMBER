using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CanJump : MonoBehaviour
{
	Rigidbody body;
	public float jumpPower = 400;

	//[Header("Debug")]
	bool onGround;

	void Start ()
	{
		body = this.GetComponent<Rigidbody>();
	}

	void FixedUpdate ()
	{
		if(!body)
		{
			Debug.LogWarning("Rigidbody component is missing from "+this.name);
			return;
		}

		if(Input.GetButtonDown("Jump"))
		{
			if(onGround && Mathf.Abs(body.linearVelocity.y - 0) < 0.05f)
			{
				body.AddForce(new Vector3(0,jumpPower,0));
				onGround = false;
			}
		}
	}

	void OnCollisionStay(Collision col)
	{
		onGround = true;
	}

	void OnCollisionExit(Collision col)
	{
		onGround = false;
	}
}
