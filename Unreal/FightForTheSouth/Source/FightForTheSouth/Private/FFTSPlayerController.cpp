#include "FFTSPlayerController.h"
#include "EngineUtils.h"
#include "FFTSFightDirector.h"
#include "InputCoreTypes.h"
#include "GameFramework/ForceFeedbackEffect.h"

void AFFTSPlayerController::BeginPlay()
{
	Super::BeginPlay();
	SetInputMode(FInputModeGameOnly());
	bShowMouseCursor = false;
}

AFFTSFightDirector* AFFTSPlayerController::Director()
{
	if (!CachedDirector.IsValid())
		for (TActorIterator<AFFTSFightDirector> It(GetWorld()); It; ++It) { CachedDirector = *It; break; }
	return CachedDirector.Get();
}

void AFFTSPlayerController::Rumble(float Intensity, float Duration, bool bHeavy)
{
	if (!bUsingGamepad) return;
	PlayDynamicForceFeedback(FMath::Clamp(Intensity, 0.f, 1.f), Duration, bHeavy, !bHeavy, bHeavy, !bHeavy, EDynamicForceFeedbackAction::Start);
}

void AFFTSPlayerController::UpdateInputDevice()
{
	static const FKey PadKeys[] = {
		EKeys::Gamepad_FaceButton_Bottom, EKeys::Gamepad_FaceButton_Right, EKeys::Gamepad_FaceButton_Left, EKeys::Gamepad_FaceButton_Top,
		EKeys::Gamepad_LeftShoulder, EKeys::Gamepad_RightShoulder, EKeys::Gamepad_LeftTrigger, EKeys::Gamepad_RightTrigger,
		EKeys::Gamepad_DPad_Up, EKeys::Gamepad_DPad_Down, EKeys::Gamepad_DPad_Left, EKeys::Gamepad_DPad_Right,
		EKeys::Gamepad_Special_Left, EKeys::Gamepad_Special_Right, EKeys::Gamepad_LeftThumbstick, EKeys::Gamepad_RightThumbstick };
	static const FKey KeyboardKeys[] = {
		EKeys::A, EKeys::D, EKeys::J, EKeys::K, EKeys::L, EKeys::U, EKeys::I, EKeys::O, EKeys::T, EKeys::SpaceBar,
		EKeys::LeftShift, EKeys::RightShift, EKeys::Left, EKeys::Right, EKeys::Tab, EKeys::Q, EKeys::V, EKeys::N, EKeys::R };
	for (const FKey& K : PadKeys)
		if (WasInputKeyJustPressed(K)) { bUsingGamepad = true; return; }
	if (FMath::Abs(GetInputAnalogKeyState(EKeys::Gamepad_LeftX)) > 0.35f || GetInputAnalogKeyState(EKeys::Gamepad_LeftTriggerAxis) > 0.4f)
	{
		bUsingGamepad = true;
		return;
	}
	for (const FKey& K : KeyboardKeys)
		if (WasInputKeyJustPressed(K)) { bUsingGamepad = false; return; }
}

void AFFTSPlayerController::PlayerTick(float DeltaTime)
{
	Super::PlayerTick(DeltaTime);
	UpdateInputDevice();
	AFFTSFightDirector* D = Director();
	if (!D) return;

	auto Tap = [this](const FKey& A, const FKey& B) { return WasInputKeyJustPressed(A) || WasInputKeyJustPressed(B); };
	if (Tap(EKeys::Tab, EKeys::Gamepad_RightThumbstick)) { D->CyclePlayerStyle(false); return; }
	if (Tap(EKeys::Q, EKeys::Gamepad_LeftThumbstick)) { D->CyclePlayerStyle(true); return; }
	if (Tap(EKeys::V, EKeys::Gamepad_DPad_Down)) { D->CycleOpponentStyle(); return; }
	if (Tap(EKeys::N, EKeys::Gamepad_Special_Left)) { D->NextOpponent(); return; }
	if (Tap(EKeys::R, EKeys::Gamepad_Special_Right)) { D->StartFight(); return; }

	FFFTSFightButtons B;
	using ffts::Command;
	if (Tap(EKeys::J, EKeys::Gamepad_FaceButton_Left)) B.Pressed = Command::Light;
	else if (Tap(EKeys::K, EKeys::Gamepad_FaceButton_Top)) B.Pressed = Command::Heavy;
	else if (Tap(EKeys::L, EKeys::Gamepad_FaceButton_Right)) B.Pressed = Command::Kick;
	else if (Tap(EKeys::U, EKeys::Gamepad_RightShoulder)) B.Pressed = Command::Grapple;
	else if (Tap(EKeys::SpaceBar, EKeys::Gamepad_FaceButton_Bottom)) B.Pressed = Command::Dodge;
	else if (Tap(EKeys::I, EKeys::Gamepad_LeftShoulder)) B.Pressed = Command::Counter;
	else if (Tap(EKeys::O, EKeys::Gamepad_RightTrigger)) B.Pressed = Command::Finisher;
	else if (Tap(EKeys::T, EKeys::Gamepad_DPad_Up)) B.Pressed = Command::Taunt;

	float Move = 0.f;
	if (IsInputKeyDown(EKeys::D) || IsInputKeyDown(EKeys::Right) || IsInputKeyDown(EKeys::Gamepad_DPad_Right)) Move += 1.f;
	if (IsInputKeyDown(EKeys::A) || IsInputKeyDown(EKeys::Left) || IsInputKeyDown(EKeys::Gamepad_DPad_Left)) Move -= 1.f;
	const float Stick = GetInputAnalogKeyState(EKeys::Gamepad_LeftX);
	if (FMath::Abs(Stick) > 0.25f) Move = Stick;
	B.ScreenMove = FMath::Clamp(Move, -1.f, 1.f);
	B.bBlock = IsInputKeyDown(EKeys::LeftShift) || IsInputKeyDown(EKeys::RightShift) || GetInputAnalogKeyState(EKeys::Gamepad_LeftTriggerAxis) > 0.4f;
	D->SetButtons(B);
}
