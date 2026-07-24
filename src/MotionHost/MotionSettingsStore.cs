using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml.Linq;

namespace MotionHost
{
    internal sealed class MotionSettingsStore
    {
        private readonly object syncRoot =
            new object();
        private readonly Dictionary<string, AxisSettings>
            settings =
                new Dictionary<string, AxisSettings>();
        private readonly string filePath;

        internal MotionSettingsStore()
        {
            string applicationData =
                Environment.GetFolderPath(
                    Environment.SpecialFolder
                        .LocalApplicationData);

            string settingsDirectory =
                Path.Combine(
                    applicationData,
                    "ThermoVision");

            filePath =
                Path.Combine(
                    settingsDirectory,
                    "motion-settings.xml");

            Load();
        }

        internal bool TryGetZero(
            int controllerNumber,
            int axis,
            out float rawZeroPosition)
        {
            lock (syncRoot)
            {
                AxisSettings axisSettings;

                if (settings.TryGetValue(
                        CreateKey(
                            controllerNumber,
                            axis),
                        out axisSettings) &&
                    axisSettings.HasZero)
                {
                    rawZeroPosition =
                        axisSettings.RawZeroPosition;
                    return true;
                }
            }

            rawZeroPosition = 0;
            return false;
        }

        internal bool TryGetLimits(
            int controllerNumber,
            int axis,
            out float minimum,
            out float maximum)
        {
            lock (syncRoot)
            {
                AxisSettings axisSettings;

                if (settings.TryGetValue(
                        CreateKey(
                            controllerNumber,
                            axis),
                        out axisSettings) &&
                    axisSettings.HasLimits)
                {
                    minimum =
                        axisSettings.Minimum;
                    maximum =
                        axisSettings.Maximum;
                    return true;
                }
            }

            minimum = 0;
            maximum = 0;
            return false;
        }

        internal void SetZero(
            int controllerNumber,
            int axis,
            float rawZeroPosition)
        {
            ValidateFinite(
                rawZeroPosition,
                "rawZeroPosition");

            lock (syncRoot)
            {
                AxisSettings axisSettings =
                    GetOrCreate(
                        controllerNumber,
                        axis);

                bool previousHasZero =
                    axisSettings.HasZero;
                float previousRawZeroPosition =
                    axisSettings.RawZeroPosition;

                axisSettings.HasZero = true;
                axisSettings.RawZeroPosition =
                    rawZeroPosition;

                try
                {
                    SaveLocked();
                }
                catch
                {
                    axisSettings.HasZero =
                        previousHasZero;
                    axisSettings.RawZeroPosition =
                        previousRawZeroPosition;
                    throw;
                }
            }
        }

        internal void RemoveZero(
            int controllerNumber,
            int axis)
        {
            lock (syncRoot)
            {
                AxisSettings axisSettings =
                    GetOrCreate(
                        controllerNumber,
                        axis);

                bool previousHasZero =
                    axisSettings.HasZero;
                float previousRawZeroPosition =
                    axisSettings.RawZeroPosition;

                axisSettings.HasZero = false;
                axisSettings.RawZeroPosition = 0;

                try
                {
                    SaveLocked();
                }
                catch
                {
                    axisSettings.HasZero =
                        previousHasZero;
                    axisSettings.RawZeroPosition =
                        previousRawZeroPosition;
                    throw;
                }
            }
        }

        internal void SetLimits(
            int controllerNumber,
            int axis,
            float minimum,
            float maximum)
        {
            ValidateFinite(
                minimum,
                "minimum");
            ValidateFinite(
                maximum,
                "maximum");

            if (minimum >= maximum)
            {
                throw new ArgumentException(
                    "软件限位最小值必须小于最大值。");
            }

            if (minimum < 0)
            {
                throw new ArgumentException(
                    "软件限位最小值不能小于 0；当前软件坐标从正限位向负方向递增。");
            }

            lock (syncRoot)
            {
                AxisSettings axisSettings =
                    GetOrCreate(
                        controllerNumber,
                        axis);

                bool previousHasLimits =
                    axisSettings.HasLimits;
                float previousMinimum =
                    axisSettings.Minimum;
                float previousMaximum =
                    axisSettings.Maximum;

                axisSettings.HasLimits = true;
                axisSettings.Minimum = minimum;
                axisSettings.Maximum = maximum;

                try
                {
                    SaveLocked();
                }
                catch
                {
                    axisSettings.HasLimits =
                        previousHasLimits;
                    axisSettings.Minimum =
                        previousMinimum;
                    axisSettings.Maximum =
                        previousMaximum;
                    throw;
                }
            }
        }

        private AxisSettings GetOrCreate(
            int controllerNumber,
            int axis)
        {
            string key =
                CreateKey(
                    controllerNumber,
                    axis);

            AxisSettings axisSettings;

            if (!settings.TryGetValue(
                key,
                out axisSettings))
            {
                axisSettings =
                    new AxisSettings(
                        controllerNumber,
                        axis);

                settings.Add(
                    key,
                    axisSettings);
            }

            return axisSettings;
        }

        private void Load()
        {
            if (!File.Exists(filePath))
            {
                return;
            }

            try
            {
                XDocument document =
                    XDocument.Load(filePath);

                XElement root =
                    document.Element(
                        "MotionSettings");

                if (root == null)
                {
                    return;
                }

                foreach (XElement element
                    in root.Elements("Axis"))
                {
                    int controllerNumber;
                    int axis;

                    if (!TryReadInt(
                            element,
                            "controller",
                            out controllerNumber) ||
                        !TryReadInt(
                            element,
                            "axis",
                            out axis) ||
                        !IsKnownAxis(
                            controllerNumber,
                            axis))
                    {
                        continue;
                    }

                    AxisSettings axisSettings =
                        new AxisSettings(
                            controllerNumber,
                            axis);

                    float value;

                    if (TryReadFloat(
                        element,
                        "rawZero",
                        out value))
                    {
                        axisSettings.HasZero = true;
                        axisSettings.RawZeroPosition =
                            value;
                    }

                    float minimum;
                    float maximum;

                    if (TryReadFloat(
                            element,
                            "minimum",
                            out minimum) &&
                        TryReadFloat(
                            element,
                            "maximum",
                            out maximum) &&
                        minimum >= 0 &&
                        minimum < maximum)
                    {
                        axisSettings.HasLimits = true;
                        axisSettings.Minimum = minimum;
                        axisSettings.Maximum = maximum;
                    }

                    settings[
                        CreateKey(
                            controllerNumber,
                            axis)] =
                                axisSettings;
                }
            }
            catch
            {
                // 损坏的配置不会阻止服务启动；轴会显示未回零或未配置限位。
            }
        }

        private void SaveLocked()
        {
            string directory =
                Path.GetDirectoryName(filePath);

            Directory.CreateDirectory(directory);

            XElement root =
                new XElement(
                    "MotionSettings");

            foreach (AxisSettings axisSettings
                in settings.Values)
            {
                if (!axisSettings.HasZero &&
                    !axisSettings.HasLimits)
                {
                    continue;
                }

                XElement element =
                    new XElement(
                        "Axis",
                        new XAttribute(
                            "controller",
                            axisSettings
                                .ControllerNumber),
                        new XAttribute(
                            "axis",
                            axisSettings.Axis));

                if (axisSettings.HasZero)
                {
                    element.Add(
                        new XAttribute(
                            "rawZero",
                            axisSettings
                                .RawZeroPosition
                                .ToString(
                                    "R",
                                    CultureInfo
                                        .InvariantCulture)));
                }

                if (axisSettings.HasLimits)
                {
                    element.Add(
                        new XAttribute(
                            "minimum",
                            axisSettings.Minimum
                                .ToString(
                                    "R",
                                    CultureInfo
                                        .InvariantCulture)));
                    element.Add(
                        new XAttribute(
                            "maximum",
                            axisSettings.Maximum
                                .ToString(
                                    "R",
                                    CultureInfo
                                        .InvariantCulture)));
                }

                root.Add(element);
            }

            XDocument document =
                new XDocument(root);

            string temporaryPath =
                filePath + ".tmp";

            document.Save(temporaryPath);

            if (File.Exists(filePath))
            {
                File.Replace(
                    temporaryPath,
                    filePath,
                    null);
            }
            else
            {
                File.Move(
                    temporaryPath,
                    filePath);
            }
        }

        private static string CreateKey(
            int controllerNumber,
            int axis)
        {
            return controllerNumber +
                ":" +
                axis;
        }

        private static bool IsKnownAxis(
            int controllerNumber,
            int axis)
        {
            if (controllerNumber < 1 ||
                controllerNumber > 3 ||
                axis < 0)
            {
                return false;
            }

            return controllerNumber == 3
                ? axis <= 2
                : axis <= 1;
        }

        private static bool TryReadInt(
            XElement element,
            string name,
            out int value)
        {
            value = 0;

            XAttribute attribute =
                element.Attribute(name);

            return attribute != null &&
                int.TryParse(
                    attribute.Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out value);
        }

        private static bool TryReadFloat(
            XElement element,
            string name,
            out float value)
        {
            value = 0;

            XAttribute attribute =
                element.Attribute(name);

            return attribute != null &&
                float.TryParse(
                    attribute.Value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value) &&
                !float.IsNaN(value) &&
                !float.IsInfinity(value);
        }

        private static void ValidateFinite(
            float value,
            string parameterName)
        {
            if (float.IsNaN(value) ||
                float.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "数值必须是有限值。");
            }
        }

        private sealed class AxisSettings
        {
            internal AxisSettings(
                int controllerNumber,
                int axis)
            {
                ControllerNumber =
                    controllerNumber;
                Axis = axis;
            }

            internal int ControllerNumber
            {
                get;
                private set;
            }

            internal int Axis
            {
                get;
                private set;
            }

            internal bool HasZero
            {
                get;
                set;
            }

            internal float RawZeroPosition
            {
                get;
                set;
            }

            internal bool HasLimits
            {
                get;
                set;
            }

            internal float Minimum
            {
                get;
                set;
            }

            internal float Maximum
            {
                get;
                set;
            }
        }
    }
}
