﻿#define _CRT_SECURE_NO_WARNINGS
#define WIN32_NO_STATUS
#include <Windows.h>
#undef WIN32_NO_STATUS
#include <winternl.h>
#include <ntstatus.h>
#include <climits>

struct FILE_GET_EA_INFORMATION {
	ULONG NextEntryOffset;
	UCHAR EaNameLength;
	CHAR EaName[1];
};

struct FILE_FULL_EA_INFORMATION {
	ULONG NextEntryOffset;
	UCHAR Flags;
	UCHAR EaNameLength;
	USHORT EaValueLength;
	CHAR EaName[1];
};

extern "C" NTSYSAPI NTSTATUS NTAPI NtQueryEaFile(
	_In_ HANDLE FileHandle,
	_Out_ PIO_STATUS_BLOCK IoStatusBlock,
	_Out_ PVOID Buffer,
	_In_ ULONG Length,
	_In_ BOOLEAN ReturnSingleEntry,
	_In_opt_ PVOID EaList,
	_In_ ULONG EaListLength,
	_In_opt_ PULONG EaIndex,
	_In_ BOOLEAN RestartScan
);

extern "C" NTSYSAPI NTSTATUS NTAPI NtSetEaFile(
	_In_ HANDLE FileHandle,
	_Out_ PIO_STATUS_BLOCK IoStatusBlock,
	_In_ PVOID EaBuffer,
	_In_ ULONG EaBufferSize
);

extern "C" NTSYSAPI BOOLEAN NTAPI RtlDosPathNameToNtPathName_U(
	_In_ PWSTR DosFileName,
	_Out_ PUNICODE_STRING NtFileName,
	_Out_opt_ PWSTR* FilePart,
	_Out_opt_ PVOID RelativeName
);

extern "C" __declspec(dllexport) HANDLE GetFileHandle(LPWSTR path, bool directory, bool create, bool write) {
	UNICODE_STRING ntPath;
	if (!RtlDosPathNameToNtPathName_U(path, &ntPath, nullptr, nullptr))
		return INVALID_HANDLE_VALUE;

	OBJECT_ATTRIBUTES objAttrs;
	InitializeObjectAttributes(&objAttrs, &ntPath, 0, 0, nullptr);

	HANDLE hFile;
	IO_STATUS_BLOCK status;
	auto res = NtCreateFile(&hFile,
		FILE_GENERIC_READ | (write ? FILE_GENERIC_WRITE : 0),
		&objAttrs, &status, nullptr, 0, 0,
		create ? FILE_CREATE : FILE_OPEN,
		FILE_SYNCHRONOUS_IO_ALERT | (directory ? FILE_DIRECTORY_FILE : FILE_NON_DIRECTORY_FILE),
		nullptr, 0
	);

	RtlFreeUnicodeString(&ntPath);
	return res == STATUS_SUCCESS ? hFile : INVALID_HANDLE_VALUE;
}

const char *LxssEaName = "LXATTRB";
const int LxssEaNameLength = 7;

extern "C" __declspec(dllexport) bool CopyLxssEa(HANDLE hFrom, HANDLE hTo) {
	const int getEaInfoSize = (int)(sizeof(FILE_GET_EA_INFORMATION) + LxssEaNameLength);
	const int eaInfoSize = (int)(sizeof(FILE_FULL_EA_INFORMATION) + LxssEaNameLength + USHRT_MAX);

	char getEaBuf[getEaInfoSize];
	auto getEaInfo = (FILE_GET_EA_INFORMATION *)getEaBuf;
	getEaInfo->NextEntryOffset = 0;
	getEaInfo->EaNameLength = LxssEaNameLength;
	strcpy(getEaInfo->EaName, LxssEaName);

	char eaBuf[eaInfoSize];
	auto eaInfo = (FILE_FULL_EA_INFORMATION *)eaBuf;
	IO_STATUS_BLOCK status;
	auto res = NtQueryEaFile(hFrom, &status, eaInfo, eaInfoSize, true, getEaInfo, getEaInfoSize, nullptr, true);
	if (res != STATUS_SUCCESS) return false;
	res = NtSetEaFile(hTo, &status, eaInfo, eaInfoSize);
	if (res != STATUS_SUCCESS) return false;

	return true;
}

extern "C" __declspec(dllexport) bool SetLxssEa(HANDLE hFile, char *data, int dataLength) {
	const int eaInfoSize = (int)(sizeof(FILE_FULL_EA_INFORMATION) + LxssEaNameLength + dataLength);
	auto eaInfo = (FILE_FULL_EA_INFORMATION *)new char[eaInfoSize];
	eaInfo->NextEntryOffset = 0;
	eaInfo->Flags = 0;
	eaInfo->EaNameLength = LxssEaNameLength;
	eaInfo->EaValueLength = dataLength;
	strcpy(eaInfo->EaName, LxssEaName);
	memcpy(eaInfo->EaName + LxssEaNameLength + 1, data, dataLength);

	IO_STATUS_BLOCK status;
	auto res = NtSetEaFile(hFile, &status, eaInfo, eaInfoSize);
	if (res != STATUS_SUCCESS) return false;

	delete eaInfo;
	return true;
}
