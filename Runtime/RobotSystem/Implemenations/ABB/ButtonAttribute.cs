using System;
using UnityEngine;

namespace RobotSystem.ABB
{
    [AttributeUsage(AttributeTargets.Field)]
    public class ButtonAttribute : PropertyAttribute
    {
        public readonly string buttonText;

        public ButtonAttribute(string text)
        {
            buttonText = text;
        }
    }
}