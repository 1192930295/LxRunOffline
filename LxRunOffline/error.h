﻿#pragma once
#include "stdafx.h"

enum err_msg {
	err_open_file,
	err_open_dir,
	err_create_file,
	err_create_dir,
	err_delete_file,
	err_delete_dir,
	err_enum_dir,
	err_file_info,
	err_get_ea,
	err_set_ea,
	err_set_cs,
	err_hard_link,
	err_read_file,
	err_write_file,
	err_transform_path,
	err_from_utf8,
	err_archive,
	err_get_version,
	err_version_old,
	err_open_key,
	err_delete_key,
	err_enum_key,
	err_copy_key,
	err_get_key_value,
	err_set_key_value,
	err_create_guid,
	err_convert_guid,
	err_distro_not_found,
	err_distro_exists,
	err_no_action,
	err_invalid_action,
	err_no_wslapi
};

class err {
public:
	err_msg msg_code;
	std::vector<wstr> msg_args;
	uint32_t err_code;
	wstr mod;
	wstr format() const;
	void push_if_empty(crwstr arg);
};

err error_last(err_msg msg_code, const std::vector<wstr> &msg_args);
err error_code(err_msg msg_code, const std::vector<wstr> &msg_args, uint32_t err_code, bool from_nt = false);
err error_other(err_msg msg_code, const std::vector<wstr> &msg_args);
