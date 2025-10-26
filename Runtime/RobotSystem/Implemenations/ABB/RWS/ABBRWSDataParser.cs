using System;
using System.Xml;
using UnityEngine;
using RobotSystem.Interfaces;
using RobotSystem.Core;

namespace RobotSystem.ABB.RWS
{
    public class ABBRWSDataParser : IRobotDataParser
    {

        private XmlNamespaceManager nsmgr;
        private string ns;
        public bool CanParse(string rawData)
        {
            return !string.IsNullOrEmpty(rawData) &&
                   rawData.Contains("<?xml") &&
                   (rawData.Contains("rap-ctrlexecstate-ev") ||
                    rawData.Contains("rap-pp-ev") ||
                    rawData.Contains("ios-signalstate-ev") ||
                    rawData.Contains("pnl-ctrlstate-ev"));
        }

        public void ParseData(string xmlData, RobotState robotState)
        {
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(xmlData);

                // Store namespace and manager in class fields
                ns = doc.DocumentElement.NamespaceURI;
                nsmgr = new XmlNamespaceManager(doc.NameTable);
                nsmgr.AddNamespace("x", ns);

                // Select <li> nodes with namespace prefix
                XmlNodeList eventNodes = doc.SelectNodes("//x:li", nsmgr);
                
                foreach (XmlNode node in eventNodes)
                {
                    string className = node.Attributes?["class"]?.Value;

                    switch (className)
                    {
                        case "rap-ctrlexecstate-ev":
                            ParseExecutionState(node, robotState); // Program Execution State
                            break;

                        case "rap-pp-ev":
                            ParseProgramPointer(node, robotState); // Rapid Program Pointer
                            break;

                        case "ios-signalstate-ev":
                            ParseIOSignal(node, robotState); // IO Signalstate
                            break;

                        case "pnl-ctrlstate-ev":
                            ParseControllerState(node, robotState); // Control State (Motor on/off)
                            break;

                        case "rap-execcycle-ev":
                            ParseExecutionCycle(node, robotState); // RAPID Execution Cycle - /rw/rapid/execution;rapidexeccycle
                            break;

                        default:
                            Debug.LogWarning($"No parser for: {className}");
                            break;
                    }

                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"ABB RWS Parser Error: {ex.Message}");
            }
        }

        private void ParseExecutionState(XmlNode node, RobotState robotState)
        {
            XmlNode stateNode = node.SelectSingleNode(".//x:span[@class='ctrlexecstate']", nsmgr);
            if (stateNode != null)
            {
                string state = stateNode.InnerText;
                robotState.UpdateMotorState(state);
                // Debug.Log($"[ABB State] Motor: {state}");
            }
        }

        private void ParseProgramPointer(XmlNode node, RobotState robotState)
        {
            string module = GetSpanText(node, "module-name");
            string routine = GetSpanText(node, "routine-name");
            string lineStr = GetSpanText(node, "BegPosLine");
            string colStr = GetSpanText(node, "BegPosCol");

            if (!string.IsNullOrEmpty(module) && !string.IsNullOrEmpty(routine))
            {
                int line = int.TryParse(lineStr, out int l) ? l : 0;
                int col = int.TryParse(colStr, out int c) ? c : 0;

                robotState.UpdateProgramPointer(module, routine, line, col);
                // Debug.Log($"[ABB State] Program: {module}.{routine}:{line}:{col}");
            }
        }

        private void ParseIOSignal(XmlNode node, RobotState robotState)
        {
            string title = node.Attributes?["title"]?.Value;

            if (!string.IsNullOrEmpty(title))
            {
                string value = GetSpanText(node, "lvalue");
                string state = GetSpanText(node, "lstate");
                string quality = GetSpanText(node, "quality");

                // Extract signal name from title
                string signalName = ExtractSignalName(title);

                // Parse value based on signal type
                object parsedValue = ParseSignalValue(value, signalName);

                robotState.UpdateIOSignal(signalName, parsedValue, state, quality);
                // Debug.Log($"[ABB State] IO {signalName}: {parsedValue} ({state}, {quality})");
            }
        }

        private void ParseControllerState(XmlNode node, RobotState robotState)
        {
            XmlNode stateNode = node.SelectSingleNode(".//x:span[@class='ctrlstate']",nsmgr);

            if (stateNode != null)
            {
                string state = stateNode.InnerText;
                robotState.UpdateControllerState(state);
                //Debug.Log($"[ABB State] Controller: {state}");
            }
        }

        private void ParseExecutionCycle(XmlNode node, RobotState robotState)
        {
            XmlNode stateNode = node.SelectSingleNode(".//x:span[@class='rapidexeccycle']", nsmgr);
            if (stateNode != null)
            {
                string state = stateNode.InnerText;
                robotState.UpdateExecutionCycle(state);
                //Debug.Log($"[ABB State] Rapid Execution Cycle: {state}");
            }
        }

        private string ExtractSignalName(string title)
        {
            string[] titleParts = title.Split('/');
            if (titleParts.Length > 0)
            {
                return titleParts[titleParts.Length - 1];
            }
            return "unknown";
        }

        private object ParseSignalValue(string value, string signalName)
        {
            // Digital outputs/inputs are typically 0/1
            if (signalName.StartsWith("DO_") || signalName.StartsWith("DI_"))
            {
                return value == "1";
            }

            // Analog signals might be numeric
            if (signalName.StartsWith("AO_") || signalName.StartsWith("AI_"))
            {
                if (float.TryParse(value, out float floatValue))
                    return floatValue;
            }

            // Default to string
            return value;
        }

        private string GetSpanText(XmlNode parentNode, string className)
        {
            XmlNode span = parentNode.SelectSingleNode($".//x:span[@class='{className}']", nsmgr);
            return span?.InnerText ?? "";
        }
    }
}