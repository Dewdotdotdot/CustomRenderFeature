# CustomRenderFeature for DepthTexture and CameraNormalTexure
Work on 2021.3.f51

참고 자료로만 사용

연습용 RenderFeature이며 2021.3.xxx버전 이외의 다른 버전에서는 작동을 안할 수 있음
정리와 최적화가 제대로 되어있는 코드가 아님
메인 카메라가 아닌 특정 오브젝트만 렌더하는 다른 카메라에 사용하는 목적으로 만들어진 코드임

If UNITY's version is 2022.1 or newer, RenderTargetIdentifier and RenderTargetHandle has to be replaced with RTHandle

shaderTagIds에 UniversalForward, UniversalForwardOnly, Universal2D까지 전부 들어가 있음. 필요에 따라 바꿔야됨

camera.projectionMatrix가 _ClipToView로 쉐이더의 글로벌 프로퍼티로 전달되게 되어있음

문제점
무엇이 문제인지 Blit(cmd, source, depthHandle.Identifier())를 안하면 depthHandle이 출력이 안됨
Feature의 AddRenderPasses에서 Setup하면 출력이 안됨. OnCameraSetup에서 source를 지정해야 CameraNormalTexture가 정상적으로 출력됨

2022버전에서는 
RenderTargetIdentifier, RenderTargetHandle를 삭제하고 RTHandle로 바꿔야됨
RenderingUtils.ReAllocateIfNeeded로 depthHandle과 normalHandle의 타겟 이름을 변경해야됨





// 12/09/2024     addded unity 2022.3x version

Depth2Global.cs
2022버전에 맞게 RTHandle 기준으로 변경
RenderQueueRange.All로 두어야 SpriteRenderer의 Depth와 DepthNormal이 렌더링됨

CustomPostProcessingFeature.cs
Postprocess Materail을 Blit하는 부분만 없음 
각 스크립트에서 cmd.blit()에 포스트 프로세스 적용


Depth2Global.cs
Change based on RTHANDLE for 2022 version
Only render setting.layer's target object
RenderQueRange.All must be placed to render SpriteRender's Depth and DepthNormal

CustomPostProcessingFeature.cs
No part that blits the Postprocess Material
Apply post-processing to cmd.blit() in each script

translated by papago


// 12/25/2024

Depth2Global.cs
optimize changed

Reduce SetPass Calls
Added useLayerSetting if-else	
