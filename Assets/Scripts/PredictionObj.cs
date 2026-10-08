using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//public enum PredictionType { KingDuration}

[CreateAssetMenu]
public class PredictionObj : ScriptableObject
{
    [SerializeField] public PredictionType PredictionType;

    [SerializeField] public string Title;

    [SerializeField] private List<string> _outcomeTitles;

    [SerializeField] public int PredictionWindowSec;

    [SerializeField] public int MinutesDuration;
    [SerializeField] public int Value;

    public List<string> GetOutcomes()
    {
        List<string> outcomes = new List<string>();
        foreach(var outcomeTitle in _outcomeTitles)
            outcomes.Add(outcomeTitle.TruncateString(25));
        return outcomes;
    }


}
