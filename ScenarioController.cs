using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Assets.LSL4Unity.Scripts;
using Valve.VR;
using Valve.VR.InteractionSystem;

public class ScenarioController : MonoBehaviour {
    //The global templates of the objects to pick and place
    public GameObject templates;

    internal List<string> blockSequence;

    //The game objects representing the targets where to place the picked objects
    public GameObject targets; 

    //The table on which the game is played
    public GameObject Table;

    //The round represents the number of the trial within the current phase
    int round = -1;

    public GameObject TrialText;
    public GameObject EndText;
    public GameObject ExpEndText;
    
    //private string condition;

    internal ErrorManager errorManager;

    internal LSLMarkerStream markerStream;

    //This game object will contained the object to move, created from one of the templates
    internal GameObject toy = null;
    private System.Random random;

    public int n_block = 0;
    public int n_trials = 162;

    string path = "Assets/Resources/Sequence";

    internal StreamWriter sequenceFile;

    internal Color[] colorMap = { Color.magenta, Color.yellow, Color.blue };

    //0 = shape, 1 = color, 2 = owner
    internal int ruleFlag = 0;
    //variable that ranges 2-4 to for rule switch
    int ruleSwap = 0;

    //rule?

    internal int toyLifespan = 0;
    internal Vector3 toySpawnPos = Vector3.zero;
    internal Quaternion toySpawnRot;

    internal Boolean collisionCheckFlag = false;

    const int tutorialTrials = 15;
    const int baselineTrialsPerRule = 13;
    const int baselineTrials = baselineTrialsPerRule * 3;
    const int mainFreezeErrors = 10;
    const int mainRuleSwitchMin = 8;
    const int mainRuleSwitchMax = 10;
    List<int> sequence = new List<int>();
    int trialCounter = 0;

    //for the tutorial experiment
    private bool tutorialFlag = true;
    private bool baselineFlag = false;
    private int[] baselineRuleOrder = new int[] { 0, 1, 2 };
    private bool mainBreakShown = false;
    private HashSet<int> mainSwitchAfterTrials = new HashSet<int>();

    private int GetRandomNextRule(int currentRule)
    {
        int nextRule = random.Next(3);
        while (nextRule == currentRule)
        {
            nextRule = random.Next(3);
        }
        return nextRule;
    }

    private int[] GetShuffledRuleOrder()
    {
        int[] order = new int[] { 0, 1, 2 };
        for (int i = order.Length - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            int temp = order[i];
            order[i] = order[j];
            order[j] = temp;
        }
        return order;
    }

    private List<int> BuildMainSwitchPoints(int totalMainTrials)
    {
        List<int> switchPoints = new List<int>();
        int completedTrials = 0;

        while (completedTrials + mainRuleSwitchMin < totalMainTrials)
        {
            int interval = random.Next(mainRuleSwitchMax - mainRuleSwitchMin + 1) + mainRuleSwitchMin;
            int nextSwitchAfter = completedTrials + interval;
            if (nextSwitchAfter >= totalMainTrials)
            {
                break;
            }

            switchPoints.Add(nextSwitchAfter);
            completedTrials = nextSwitchAfter;
        }

        return switchPoints;
    }

    private int PickFreezeTrialForSegment(int segmentStart, int segmentEnd, HashSet<int> usedTrials, List<int> switchPoints)
    {
        List<int> preferredCandidates = new List<int>();
        List<int> fallbackCandidates = new List<int>();

        for (int trial = segmentStart; trial <= segmentEnd; trial++)
        {
            int offsetFromSegmentStart = trial - segmentStart + 1;
            if (offsetFromSegmentStart < 4)
            {
                continue;
            }

            bool nearSwitch = false;
            for (int i = 0; i < switchPoints.Count; i++)
            {
                int switchPoint = switchPoints[i];
                if (trial == switchPoint || trial == switchPoint - 1 || trial == switchPoint + 1)
                {
                    nearSwitch = true;
                    break;
                }
            }

            if (nearSwitch || usedTrials.Contains(trial))
            {
                continue;
            }

            if (offsetFromSegmentStart == 5 || offsetFromSegmentStart == 6)
            {
                preferredCandidates.Add(trial);
            }
            else if (offsetFromSegmentStart >= 7)
            {
                fallbackCandidates.Add(trial);
            }
        }

        if (preferredCandidates.Count > 0)
        {
            return preferredCandidates[random.Next(preferredCandidates.Count)];
        }

        if (fallbackCandidates.Count > 0)
        {
            return fallbackCandidates[random.Next(fallbackCandidates.Count)];
        }

        return -1;
    }

    private int GetTargetIndexByShape(string shapeTag)
    {
        for (int i = 0; i < targets.transform.childCount; i++)
        {
            GameObject target = targets.transform.GetChild(i).gameObject;
            if (target.tag == shapeTag)
            {
                return i;
            }
        }
        return -1;
    }

    private int GetTargetIndexByColor(Color color)
    {
        for (int i = 0; i < targets.transform.childCount; i++)
        {
            GameObject target = targets.transform.GetChild(i).gameObject;
            Color targetColor = target.transform.GetChild(0).GetComponent<Renderer>().material.color;
            if (targetColor == color)
            {
                return i;
            }
        }
        return -1;
    }

    private int GetTargetIndexByOwner(string ownerInitial)
    {
        if (string.IsNullOrWhiteSpace(ownerInitial))
        {
            return -1;
        }

        for (int i = 0; i < targets.transform.childCount; i++)
        {
            GameObject target = targets.transform.GetChild(i).gameObject;
            string targetOwner = GetOwnerInitial(target);
            if (!string.IsNullOrWhiteSpace(targetOwner)
                && string.Equals(targetOwner, ownerInitial, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private bool HasOverlappingCorrectFeatures(GameObject template, Color toyColor)
    {
        if (template == null)
        {
            return true;
        }

        int shapeTargetIndex = GetTargetIndexByShape(template.tag);
        int colorTargetIndex = GetTargetIndexByColor(toyColor);
        int ownerTargetIndex = GetTargetIndexByOwner(GetOwnerInitial(template));

        return shapeTargetIndex == colorTargetIndex
            || shapeTargetIndex == ownerTargetIndex
            || colorTargetIndex == ownerTargetIndex;
    }

    private void SelectTrialStimulus(out int idObject, out int idColour)
    {
        List<(int objectId, int colourId)> validCombinations = new List<(int objectId, int colourId)>();

        for (int objectId = 0; objectId < templates.transform.childCount; objectId++)
        {
            GameObject template = templates.transform.GetChild(objectId).gameObject;
            for (int colourId = 0; colourId < colorMap.Length; colourId++)
            {
                if (!HasOverlappingCorrectFeatures(template, colorMap[colourId]))
                {
                    validCombinations.Add((objectId, colourId));
                }
            }
        }

        if (validCombinations.Count > 0)
        {
            var selected = validCombinations[random.Next(validCombinations.Count)];
            idObject = selected.objectId;
            idColour = selected.colourId;
            return;
        }

        idObject = random.Next(templates.transform.childCount);
        idColour = random.Next(colorMap.Length);
    }

    public void EndTrial()
    {

        if (toy != null)
        {   
            Destroy(toy);
            toy = null;
            markerStream.Write("End_of_Trial");
            round = round + 1;

            // tutorial rule progression: 3 rules x 5 trials (fixed to shape -> colour -> owner)
            if (tutorialFlag)
            {
                int tutorialRule = Math.Min(2, round / 5);
                if (tutorialRule != ruleFlag)
                {
                    markerStream.Write("Switch_trial");
                    Debug.Log("Switch_trial");
                    ruleFlag = tutorialRule;
                    if (ruleFlag == 0)
                    {
                        markerStream.Write("Rule_switched_to_shape");
                        Debug.Log("Rule_switched_to_shape");
                    }
                    else if (ruleFlag == 1)
                    {
                        markerStream.Write("Rule_switched_to_colour");
                        Debug.Log("Rule_switched_to_colour");
                    }
                    else
                    {
                        markerStream.Write("Rule_switched_to_owner");
                        Debug.Log("Rule_switched_to_owner");
                    }
                }
                else
                {
                    markerStream.Write("No_switch_trial");
                    if (ruleFlag == 0)
                    {
                        markerStream.Write("No_switch_trial_shape_rule_active");
                    }
                    else if (ruleFlag == 1)
                    {
                        markerStream.Write("No_switch_trial_colour_rule_active");
                    }
                    else
                    {
                        markerStream.Write("No_switch_trial_owner_rule_active");
                    }
                }
            }
            // baseline rule progression: 3 rules x 13 trials (randomized order per session)
            else if (baselineFlag)
            {
                int baselineRuleSegment = Math.Min(2, round / baselineTrialsPerRule);
                int baselineRule = baselineRuleOrder[baselineRuleSegment];
                if (baselineRule != ruleFlag)
                {
                    markerStream.Write("Switch_trial");
                    Debug.Log("Switch_trial");
                    ruleFlag = baselineRule;
                    if (ruleFlag == 0)
                    {
                        markerStream.Write("Rule_switched_to_shape");
                        Debug.Log("Rule_switched_to_shape");
                    }
                    else if (ruleFlag == 1)
                    {
                        markerStream.Write("Rule_switched_to_colour");
                        Debug.Log("Rule_switched_to_colour");
                    }
                    else
                    {
                        markerStream.Write("Rule_switched_to_owner");
                        Debug.Log("Rule_switched_to_owner");
                    }
                }
                else
                {
                    markerStream.Write("No_switch_trial");
                    if (ruleFlag == 0)
                    {
                        markerStream.Write("No_switch_trial_shape_rule_active");
                    }
                    else if (ruleFlag == 1)
                    {
                        markerStream.Write("No_switch_trial_colour_rule_active");
                    }
                    else
                    {
                        markerStream.Write("No_switch_trial_owner_rule_active");
                    }
                }
            }
            //check if rule needs to be swapped
            else if (round > 0 && mainSwitchAfterTrials.Contains(round))
            {
                markerStream.Write("Switch_trial");
                // Debug.log can be seen by experimenters when experiment is being conducted by the subject
                Debug.Log("Switch_trial");
                ruleFlag = GetRandomNextRule(ruleFlag);
                if (ruleFlag == 0)
                {
                    markerStream.Write("Rule_switched_to_shape");
                    Debug.Log("Rule_switched_to_shape");
                }
                else if (ruleFlag == 1)
                {
                    markerStream.Write("Rule_switched_to_colour");
                    Debug.Log("Rule_switched_to_colour");
                }
                else
                {
                    markerStream.Write("Rule_switched_to_owner");
                    Debug.Log("Rule_switched_to_owner");
                }
            }
            else
            {
                markerStream.Write("No_switch_trial");
                if (ruleFlag == 0)
                {
                    markerStream.Write("No_switch_trial_shape_rule_active");
                }
                else if (ruleFlag == 1)
                {
                    markerStream.Write("No_switch_trial_colour_rule_active");
                }
                else
                {
                    markerStream.Write("No_switch_trial_owner_rule_active");
                }
            }

            //"tutorial"
            if (round >= tutorialTrials && tutorialFlag)
            {
                tutorialFlag = false;
                baselineFlag = true;
                round = 0;
                ruleFlag = baselineRuleOrder[0];
                markerStream.Write("End_of_Tutorial");
                TutorialFinished();
            }
            //"baseline"
            else if (round >= baselineTrials && baselineFlag)
            {
                baselineFlag = false;
                round = 0;
                ruleFlag = random.Next(3);
                mainBreakShown = false;
                markerStream.Write("End_of_Baseline");
                BlockFinished();
            }
            //main experiment progression
            else if (!tutorialFlag && !baselineFlag)
            {
                if (!mainBreakShown && round == (n_trials / 2))
                {
                    mainBreakShown = true;
                    markerStream.Write("Mid_Experiment_Break");
                    BlockFinished();
                }
                else if (round >= n_trials)
                {
                    markerStream.Write("End_of_Experiment");
                    ExpEndText.SetActive(true);
                    return;
                }
                else
                {
                    StartCoroutine(waitAsec());
                }
            }
            else
            {
                StartCoroutine(waitAsec());
            }
        }

    }

    IEnumerator waitAsec()
    {
        yield return new WaitForSeconds(2);

        targets.transform.GetChild(0).gameObject.GetComponent<Renderer>().material.color = Color.magenta;
        targets.transform.GetChild(1).gameObject.GetComponent<Renderer>().material.color = Color.yellow;
        targets.transform.GetChild(2).gameObject.GetComponent<Renderer>().material.color = Color.blue;

        errorManager.rotated = false;
        StartTrial();

    }

    void Start()
    {
        random = new System.Random();
        baselineRuleOrder = GetShuffledRuleOrder();
        sequence.Capacity = n_trials + tutorialTrials + baselineTrials;
        ruleSwap = random.Next(mainRuleSwitchMax - mainRuleSwitchMin + 1) + mainRuleSwitchMin;
        errorManager = gameObject.GetComponent<ErrorManager>();
        //blockSequence = new List<String>(File.ReadAllLines(path+n_block+".txt"));
        //sequenceFile = File.OpenText(path + n_block + ".txt");
        string dt = "" + DateTime.Now;
        dt = dt.Replace(":", "_");
        dt = dt.Replace(".", "_");
        sequenceFile = new StreamWriter(path + n_block + "_" + dt + ".txt", true);
        markerStream = gameObject.GetComponent<LSLMarkerStream>();
        StartBlock();
    }

    void StartBlock()
    {
        targets.transform.GetChild(0).gameObject.GetComponent<Renderer>().material.color = Color.magenta;
        targets.transform.GetChild(1).gameObject.GetComponent<Renderer>().material.color = Color.yellow;
        targets.transform.GetChild(2).gameObject.GetComponent<Renderer>().material.color = Color.blue;
        markerStream.Write("Block_Start");
        round = 0;
        StartTrial();
    }

    void TutorialFinished()
    {
        markerStream.Write("End_of_Tutorial");
        TrialText.SetActive(true);
        Debug.Log("Tutorial experiment done");
    }

    void BlockFinished() 
    {
        markerStream.Write("End_of_Block");
        EndText.SetActive(true);
        Debug.Log("Block " + (total + 1) + " done.");
    }

    // Creation of sequence list
    /// <summary>
    /// Initializes and starts a trial in the experiment, handling sequence setup, error injection, and stimulus spawning.
    /// </summary>
    /// <remarks>
    /// On the first trial (trialCounter == 0), this method:
    /// - Creates a sequence list containing tutorial trials, baseline trials, and main experiment trials
    /// - Distributes approximately 10 freeze errors evenly across the main experiment using jittered placement
    /// - Uses a HashSet to ensure no duplicate error positions
    /// 
    /// For each trial, this method:
    /// - Selects a trial stimulus (object and color)
    /// - Retrieves the error type for the current trial from the sequence
    /// - Logs the trial information to file and marker stream
    /// - Instantiates a draggable GameObject based on the selected object template
    /// - Applies the selected color to the spawned object
    /// - Initializes toy lifespan and collision detection flag
    /// <note>
    /// Error types:
    /// - 0: No Error
    /// - 1: Error of execution (freezing object)
    /// Objects: 0=Laptop, 1=Calculator, 2=Pen
    /// </note>
    void StartTrial(/*String condition*/)
    {
        if(trialCounter == 0)
        {
            sequence.Clear();
            // tutorial list
            for (int i = 0; i < tutorialTrials; i++)
            {
                sequence.Add(0);
            }
            // baseline list
            for (int i = 0; i < baselineTrials; i++)
            {
                sequence.Add(0);
            }
            // main experiment list
            for (int i = 0; i < n_trials; i++)
            {
                sequence.Add(0);
            }

            int mainStartIndex = tutorialTrials + baselineTrials;

            List<int> switchPoints = BuildMainSwitchPoints(n_trials);
            mainSwitchAfterTrials.Clear();
            for (int i = 0; i < switchPoints.Count; i++)
            {
                mainSwitchAfterTrials.Add(switchPoints[i]);
            }

            List<(int start, int end)> segments = new List<(int start, int end)>();
            int segmentStart = 1;
            for (int i = 0; i < switchPoints.Count; i++)
            {
                int segmentEnd = switchPoints[i];
                if (segmentEnd >= segmentStart)
                {
                    segments.Add((segmentStart, segmentEnd));
                }
                segmentStart = switchPoints[i] + 1;
            }
            if (segmentStart <= n_trials)
            {
                segments.Add((segmentStart, n_trials));
            }

            HashSet<int> usedErrorTrials = new HashSet<int>();
            HashSet<int> usedSegments = new HashSet<int>();

            int targetFreezeCount = Math.Min(mainFreezeErrors, segments.Count);
            for (int i = 0; i < targetFreezeCount; i++)
            {
                int desiredSegment = (int)Math.Floor(((i + 0.5) * segments.Count) / targetFreezeCount);
                desiredSegment = Math.Max(0, Math.Min(segments.Count - 1, desiredSegment));

                int selectedSegment = desiredSegment;
                int searchRadius = 0;
                while (usedSegments.Contains(selectedSegment) && searchRadius < segments.Count)
                {
                    searchRadius++;
                    int right = desiredSegment + searchRadius;
                    int left = desiredSegment - searchRadius;
                    if (right < segments.Count && !usedSegments.Contains(right))
                    {
                        selectedSegment = right;
                        break;
                    }
                    if (left >= 0 && !usedSegments.Contains(left))
                    {
                        selectedSegment = left;
                        break;
                    }
                }

                usedSegments.Add(selectedSegment);
                int freezeTrial = PickFreezeTrialForSegment(
                    segments[selectedSegment].start,
                    segments[selectedSegment].end,
                    usedErrorTrials,
                    switchPoints);
                if (freezeTrial != -1)
                {
                    usedErrorTrials.Add(freezeTrial);
                    sequence[mainStartIndex + (freezeTrial - 1)] = 1;
                }
            }

            if (usedErrorTrials.Count < mainFreezeErrors)
            {
                for (int i = 0; i < segments.Count && usedErrorTrials.Count < mainFreezeErrors; i++)
                {
                    int freezeTrial = PickFreezeTrialForSegment(segments[i].start, segments[i].end, usedErrorTrials, switchPoints);
                    if (freezeTrial != -1)
                    {
                        usedErrorTrials.Add(freezeTrial);
                        sequence[mainStartIndex + (freezeTrial - 1)] = 1;
                    }
                }
            }

            if (usedErrorTrials.Count < mainFreezeErrors)
            {
                Debug.LogWarning("Could not place all freeze events while satisfying Step 7 constraints.");
            }
        }

        //0 : Object 0 (Laptop)
        //1 : Object 1 (Calculator)
        //2 : Object 2 (Pen)
        //0 : No Error
        //1 : Error of execution (freezing object)

        int id_object;
        int id_colour;
        SelectTrialStimulus(out id_object, out id_colour);

        int id_error = sequence[trialCounter];
        trialCounter++;

        sequenceFile.Write(id_error + ":" + id_object + "\n");
        errorManager.errorType = id_error;
        sequenceFile.Flush();

        switch(id_error){
            case 0:
                markerStream.Write("Start_Iteration " + round + ": No Error");
                Debug.Log("Start_Iteration" + round + ": No Error");
                break;
            case 1:
                markerStream.Write("Start_Iteration " + round + ": Freezing Error");
                Debug.Log("Start_Iteration" + round + ": Freezing Error");
                break;
            default:
                break;
        }

        //Spawn draggable object
        GameObject template = templates.transform.GetChild(id_object).gameObject;
        toy = GameObject.Instantiate(template, Table.transform);
        toySpawnPos = template.transform.position;
        toySpawnRot = template.transform.rotation;
        toy.transform.SetPositionAndRotation(toySpawnPos, toySpawnRot);
        toy.transform.GetChild(0).GetComponent<Renderer>().material.color = colorMap[id_colour];
        toy.SetActive(true);
        toyLifespan = 3;
        collisionCheckFlag = false;
    }

	// Update is called once per frame
	void Update () {
        if (toy != null)
        {
            Valve.VR.InteractionSystem.Interactable toyInter = (Valve.VR.InteractionSystem.Interactable) toy.GetComponent("Interactable");
            if (collisionCheckFlag == false && toyInter.attachedToHand != null)
            {
                GrabbedObject();
                collisionCheckFlag = true;
            }

        }

        // If there is yet no instance of object to move, we create one
        if (EndText.activeSelf || TrialText.activeSelf)
        {
            //wait for Grab on right hand
            if (SteamVR_Actions._default.GrabGrip.GetStateDown(SteamVR_Input_Sources.RightHand) || SteamVR_Actions._default.GrabGrip.GetStateDown(SteamVR_Input_Sources.LeftHand))
            {
                if (TrialText.activeSelf)
                {
                    TrialText.SetActive(false);
                }
                if (EndText.activeSelf)
                {
                    EndText.SetActive(false);
                    total += 1;
                }
                round = 0;

                StartCoroutine(waitAsec());
            }
        }
        if (ExpEndText.activeSelf)
        {
            Debug.Log("Tell participant to stop :)");
        }
    }

    public void GrabbedObject()
    {
        markerStream.Write("Object_grabbed");
    }

    private string GetOwnerInitial(GameObject obj)
    {
        if (obj == null)
        {
            return string.Empty;
        }

        TargetProperties targetProperties = obj.GetComponent<TargetProperties>();
        if (targetProperties == null)
        {
            targetProperties = obj.GetComponentInChildren<TargetProperties>();
        }

        if (targetProperties == null || string.IsNullOrWhiteSpace(targetProperties.initial))
        {
            return string.Empty;
        }

        return targetProperties.initial.Trim();
    }

    public bool OnObjectCollided(GameObject targetObject, GameObject toyObject)
    {
        if (toy != null)
        {
            if (targetObject == null || toyObject == null)
            {
                return false;
            }

            bool isCorrectPlacement = false;
            if (ruleFlag == 0) //shape rule
            {
                isCorrectPlacement = toyObject.tag == targetObject.tag;
            }
            else if (ruleFlag == 1) //colour rule
            {
                Color targetColor = targetObject.transform.GetChild(0).GetComponent<Renderer>().material.color;
                isCorrectPlacement = toyObject.GetComponent<Renderer>().material.color == targetColor;
            }
            else //owner rule
            {
                string targetOwner = GetOwnerInitial(targetObject);
                string toyOwner = GetOwnerInitial(toyObject);
                isCorrectPlacement = !string.IsNullOrEmpty(targetOwner)
                    && !string.IsNullOrEmpty(toyOwner)
                    && string.Equals(targetOwner, toyOwner, StringComparison.OrdinalIgnoreCase);
            }

            if (isCorrectPlacement)
            {
                markerStream.Write("Correct Feedback received - no error simulated");
                toyObject.GetComponent<Renderer>().material.color = Color.cyan;
            }
            else
            {
                markerStream.Write("Wrong Feedback received - no error simulated");
                toyObject.GetComponent<Renderer>().material.color = Color.red;
            }
            markerStream.Write("E0");
            EndTrial();
            return true;
        }
        return false;
    }

    public bool OnCollisionCheck()
    {
        if (!toy) return false;
        if(collisionCheckFlag == false)
        {
            return false;
        }
        //this takes ages, Idk why the collision detection is so infrequent
        //Debug.Log("OnCollisionCheck" + toyLifespan);
        if(--toyLifespan == 0)
        {
            toy.transform.SetPositionAndRotation(toySpawnPos, toySpawnRot);
            toyLifespan = 3;
            collisionCheckFlag = false;
        }
        return false;
    }
    
    ~ScenarioController()
    {
        sequenceFile.Close();
    }
}
