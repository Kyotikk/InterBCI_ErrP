using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CollisionHandler : MonoBehaviour {

    public ScenarioController scenarioController;

	// Use this for initialization
	void Start () {

	}
	
	// Update is called once per frame
	void Update () {
        
    }

    //collision --> target, gameObject --> attached toy
    public void OnCollisionEnterChild(Collision collision, GameObject gameObject)
    {
        if (collision == null || gameObject == null || collision.gameObject == null)
        {
            return;
        }
        StartCoroutine(waitAsec(collision, gameObject));

    }

    //collision == hand?!, gameObject == cloth
    IEnumerator waitAsec(Collision collision, GameObject gameObject)
    {
        //Debug.Log(collision.gameObject);
        string tag = collision.gameObject.tag;
        if (tag != "Untagged")
        {
            yield return new WaitForSeconds(0);
            if (scenarioController.OnObjectCollided(collision.gameObject, gameObject))
            {
                Destroy(collision.gameObject);
            }
        }
        else
        {
            scenarioController.OnCollisionCheck();
        }
    }
}
