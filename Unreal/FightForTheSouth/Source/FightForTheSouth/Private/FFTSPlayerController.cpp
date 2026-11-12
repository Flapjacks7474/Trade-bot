#include "FFTSPlayerController.h"
#include "EngineUtils.h"
#include "FFTSFightDirector.h"
#include "InputCoreTypes.h"

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

void AFFTSPlayerController::PlayerTick(float DeltaTime)
{
	Super::PlayerTick(DeltaTime);
	AFFTSFightDirector* D = Director();
	if (!D) return;

	auto Tap = [this](const FKey& A, const FKey& B) { return WasInputKeyJustPressed(A) || WasInputKeyJustPressed(B); };
	if (WasInputKeyJustPressed(EKeys::Tab)) { D->CyclePlayerStyle(false); return; }
	if (WasInputKeyJustPressed(EKeys::Q)) { D->CyclePlayerStyle(true); return; }
	if (WasInputKeyJustPressed(EKeys::V)) { D->CycleOpponentStyle(); return; }
	if (WasInputKeyJustPressed(EKeys::N)) { D->NextOpponent(); return; }
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
	if (IsInputKeyDown(EKeys::D) || IsInputKeyDown(EKeys::Right)) Move += 1.f;
	if (IsInputKeyDown(EKeys::A) || IsInputKeyDown(EKeys::Left)) Move -= 1.f;
	const float Stick = GetInputAnalogKeyState(EKeys::Gamepad_LeftX);
	if (FMath::Abs(Stick) > 0.25f) Move = Stick;
	B.ScreenMove = FMath::Clamp(Move, -1.f, 1.f);
	B.bBlock = IsInputKeyDown(EKeys::LeftShift) || IsInputKeyDown(EKeys::RightShift) || GetInputAnalogKeyState(EKeys::Gamepad_LeftTriggerAxis) > 0.4f;
	D->SetButtons(B);
}
