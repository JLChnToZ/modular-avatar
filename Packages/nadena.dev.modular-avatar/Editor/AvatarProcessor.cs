﻿/*
 * MIT License
 * 
 * Copyright (c) 2022 bd_
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 */

using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Editor;
using VRC.SDKBase.Editor.BuildPipeline;
using VRC.SDKBase.Validation.Performance.Stats;
using Object = UnityEngine.Object;

namespace nadena.dev.modular_avatar.core.editor
{
    [InitializeOnLoad]
    public class AvatarProcessor : IVRCSDKPreprocessAvatarCallback, IVRCSDKPostprocessAvatarCallback
    {
        public delegate void AvatarProcessorCallback(GameObject obj);

        public static event AvatarProcessorCallback AfterProcessing;

        static AvatarProcessor()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        [MenuItem("Tools/Modular Avatar/Apply to current avatar", false)]
        private static void ApplyToCurrentAvatar()
        {
            var avatar = Selection.activeGameObject;
            if (avatar == null || avatar.GetComponent<VRCAvatarDescriptor>() == null) return;
            var basePath = "Assets/ModularAvatarOutput/" + avatar.name;
            var savePath = basePath;

            int extension = 0;

            while (File.Exists(savePath) || Directory.Exists(savePath))
            {
                savePath = basePath + " " + (++extension);
            }

            try
            {
                Util.OverridePath = savePath;

                avatar = Object.Instantiate(avatar);
                avatar.transform.position += Vector3.forward * 2;
                ProcessAvatar(avatar);
            }
            finally
            {
                Util.OverridePath = null;
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange obj)
        {
            if (obj == PlayModeStateChange.EnteredEditMode)
            {
                Util.DeleteTemporaryAssets();
            }
        }

        public int callbackOrder => -9000;

        public void OnPostprocessAvatar()
        {
            Util.DeleteTemporaryAssets();
        }

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            try
            {
                ProcessAvatar(avatarGameObject);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError(e);
                return false;
            }
        }

        public static void ProcessAvatar(GameObject avatarGameObject)
        {
            BoneDatabase.ResetBones();
            PathMappings.Clear();

            try
            {
                new RenameParametersHook().OnPreprocessAvatar(avatarGameObject);
                new MenuInstallHook().OnPreprocessAvatar(avatarGameObject);
                new MergeArmatureHook().OnPreprocessAvatar(avatarGameObject);
                new RetargetMeshes().OnPreprocessAvatar(avatarGameObject);
                new BoneProxyProcessor().OnPreprocessAvatar(avatarGameObject);
                new VisibleHeadAccessoryProcessor(avatarGameObject.GetComponent<VRCAvatarDescriptor>()).Process();
                new MergeAnimatorProcessor().OnPreprocessAvatar(avatarGameObject);
                new BlendshapeSyncAnimationProcessor().OnPreprocessAvatar(avatarGameObject);

                AfterProcessing?.Invoke(avatarGameObject);
            }
            finally
            {
                // Ensure that we clean up AvatarTagComponents after failed processing. This ensures we don't re-enter
                // processing from the Awake method on the unprocessed AvatarTagComponents
                foreach (var component in avatarGameObject.GetComponentsInChildren<AvatarTagComponent>(true))
                {
                    UnityEngine.Object.DestroyImmediate(component);
                }
            }

            // The VRCSDK captures some debug information about animators as part of the build process, prior to invoking
            // hooks. For some reason this happens in the ValidateFeatures call on the SDK builder. Reinvoke it to
            // refresh this debug info.
            var avatar = avatarGameObject.GetComponent<VRCAvatarDescriptor>();
            var animator = avatarGameObject.GetComponent<Animator>();
            var builder = new VRCSdkControlPanelAvatarBuilder3A();
            builder.RegisterBuilder(ScriptableObject.CreateInstance<VRCSdkControlPanel>());
            builder.ValidateFeatures(avatar, animator, new AvatarPerformanceStats(false));
        }
    }
}