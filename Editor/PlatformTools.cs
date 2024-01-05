using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace PluginSet.Platform.Editor
{
    public static class PlatformTools
    {
        public static void BuildIpaInstaller(string ipa, string output, string remote)
        {
            var libPath = GetPackageFullPath("com.pluginset.tools.platform");
            var templatePath = Path.Combine(libPath, "IosTools~");

            var ipaName = string.Empty;
            if (!string.IsNullOrEmpty(ipa))
                ipaName = Path.GetFileName(ipa);

            if (!Directory.Exists(output))
                Directory.CreateDirectory(output);

            var index = File.ReadAllText(Path.Combine(templatePath, "index.html"));
            var manifest = File.ReadAllText(Path.Combine(templatePath, "manifest.plist"));

            index = index.Replace("{{DISPLAY_NAME}}", PlayerSettings.productName);
            manifest = manifest.Replace("{{DISPLAY_NAME}}", PlayerSettings.productName);
            
            index = index.Replace("{{BUNDLE_ID}}", PlayerSettings.applicationIdentifier);
            manifest = manifest.Replace("{{BUNDLE_ID}}", PlayerSettings.applicationIdentifier);
            
            index = index.Replace("{{VERSION_NAME}}", PlayerSettings.bundleVersion);
            manifest = manifest.Replace("{{VERSION_NAME}}", PlayerSettings.bundleVersion);
            
            index = index.Replace("{{VERSION_CODE}}", PlayerSettings.iOS.buildNumber);
            manifest = manifest.Replace("{{VERSION_CODE}}", PlayerSettings.iOS.buildNumber);
            
            index = index.Replace("{{ICON_URL}}", $"{remote}/app.png");
            manifest = manifest.Replace("{{ICON_URL}}", $"{remote}/app.png");
            
            index = index.Replace("{{MANIFEST_URL}}", $"{remote}/manifest.plist");
            manifest = manifest.Replace("{{MANIFEST_URL}}", $"{remote}/manifest.plist");
            
            index = index.Replace("{{IPA_URL}}", $"{remote}/{ipaName}");
            manifest = manifest.Replace("{{IPA_URL}}", $"{remote}/{ipaName}");
            
            File.WriteAllText(Path.Combine(output, "index.html"), index);
            File.WriteAllText(Path.Combine(output, "manifest.plist"), manifest);
            var outputIpa = Path.Combine(output, ipaName);
            if (!string.IsNullOrEmpty(ipa) && !ipa.Equals(outputIpa))
                File.Copy(ipa, outputIpa, true);

            var appIconPath = Path.Combine(templatePath, "app.png");
            
#if UNITY_IOS_API
            var icons = PlayerSettings.GetPlatformIcons(BuildTargetGroup.iOS, UnityEditor.iOS.iOSPlatformIconKind.Application);
            if (icons != null && icons.Length > 0)
            {
                var icon = icons[0].GetTexture();
                if (icon != null)
                {
                    var path = AssetDatabase.GetAssetPath(icon);
                    if (!string.IsNullOrEmpty(path))
                        appIconPath = path;
                }
            }
            File.Copy(appIconPath, Path.Combine(output, "app.png"), true);
#endif
        }
        
        public static void CopyGradleFiles(string targetPath)
        {
			var corePath = GetPackageFullPath("com.pluginset.tools.platform");
			var toolsPath = Path.Combine(corePath, "AndroidTools~");
			CopyFilesTo( targetPath, toolsPath, "*", SearchOption.TopDirectoryOnly);

			var wrapperPath = Path.Combine(toolsPath, "gradle", "wrapper");
			var srcPath = Path.Combine(targetPath, "gradle", "wrapper");
			CopyFilesTo( srcPath, wrapperPath, "*", SearchOption.TopDirectoryOnly);

#if UNITY_2020_3_OR_NEWER
			var gradleVersion = "gradle-6.1.1-bin";
#else
			var gradleVersion = "gradle-5.6.4-bin";
#endif
			var propertiesFile = Path.Combine(srcPath, "gradle-wrapper.properties");
			var properties = File.ReadAllLines(propertiesFile);
			properties[properties.Length - 1] = $"distributionUrl=https\\://services.gradle.org/distributions/{gradleVersion}.zip";
			File.WriteAllLines(propertiesFile, properties);
			
            if (Application.platform == RuntimePlatform.OSXEditor)
            {
	            SetFileExecutable(Path.GetFullPath(Path.Combine(targetPath, "gradlew")).Replace("\\", "/"));
            }
        }
        
        private static void SetFileExecutable(string fileName, string workDir = null)
        {
            var path = fileName;
            if (!string.IsNullOrEmpty(workDir) && !Path.IsPathRooted(path))
            {
                path = Path.Combine(workDir, fileName);
            }

            if (!File.Exists(path))
                return;
            
            ExecuteCommand("chmod", false, "u+x ", path);
        }
        
        private static void ExecuteCommand(string fileName, bool useShell, params string[] args)
        {
            Process process = new Process();
            process.StartInfo.FileName = fileName;
            process.StartInfo.Arguments = string.Join(" ", args);
            process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            process.StartInfo.RedirectStandardOutput = !useShell;
            process.StartInfo.UseShellExecute = useShell;
            process.StartInfo.CreateNoWindow = !useShell;
            process.StartInfo.ErrorDialog = true;
            process.StartInfo.WorkingDirectory = Environment.CurrentDirectory;
            process.Start();

            Debug.Log(process.StartInfo.FileName);
            Debug.Log(process.StartInfo.Arguments);

            StringBuilder exceptionInfo = null;
            if (useShell)
            {
                process.WaitForExit();
            }
            else
            {
                while (!process.StandardOutput.EndOfStream)
                {
                    string line = process.StandardOutput.ReadLine();
                    if (exceptionInfo != null)
                    {
                        exceptionInfo.AppendLine(line);
                    }
                    else
                    {
                        if (line.StartsWith("Warning:"))
                        {
                            Debug.LogWarning(line);
                        }
                        else if (line.StartsWith("Error:"))
                        {
                            Debug.LogError(line);
                        }
                        else if (line.StartsWith("Unhandled Exception:"))
                        {
                            exceptionInfo = new StringBuilder(line);
                        }
                        else
                        {
                            Debug.Log(line);
                        }
                    }
                }
                
                process.WaitForExit();
                process.Close();

                if (exceptionInfo != null)
                {
                    Debug.LogError(exceptionInfo);
                }
            }
        }
        
        private static string GetPackageFullPath(string packageName)
        {
            // Check for potential UPM package
            string packagePath = Path.GetFullPath($"Packages/{packageName.ToLower()}");
            if (Directory.Exists(packagePath))
            {
                return packagePath;
            }

            packagePath = Path.GetFullPath("Assets/..");
            if (Directory.Exists(packagePath))
            {
                // Search default location for development package
                if (Directory.Exists(packagePath + $"/Assets/Packages/{packageName}/Editor Resources"))
                {
                    return packagePath + $"/Assets/Packages/{packageName}";
                }
            }

            return null;
        }
        
        private static void CopyFilesTo(string dstPath, string srcPath, string pattern, SearchOption option = SearchOption.AllDirectories)
        {
            if (!Directory.Exists(srcPath))
                return;

            srcPath = Path.GetFullPath(srcPath);
            dstPath = Path.GetFullPath(dstPath);
            foreach (var file in Directory.GetFiles(srcPath, pattern, option))
            {
                CopyFile(file, file.Replace(srcPath, dstPath));
            }
        }
        
        private static void CopyFile(string src, string dst)
        {
            var dir = Path.GetDirectoryName(dst);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            CheckAndCopyFile(src, dst);
        }
        
        private static void CheckAndCopyFile(string src, string dst)
        {
            var dir = Path.GetDirectoryName(dst);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.Copy(src, dst, true);
        }
        
    }
}