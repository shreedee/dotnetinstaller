#include "StdAfx.h"
#include "XmlAttribute.h"
#include "Configuration.h"
#include "InstallerLog.h"
#include "InstallerSession.h"
#include "InstallConfiguration.h"
#include "MsiComponent.h"
#include "MsuComponent.h"
#include "CmdComponent.h"
#include "OpenFileComponent.h"

InstallConfiguration::InstallConfiguration()
	: Configuration(configuration_install)
    , must_reboot_required(false)
    , auto_close_if_installed(false)
    , auto_close_on_error(false)
    , dialog_show_installed(false)
    , dialog_show_required(false)
    , allow_continue_on_error(true)
	, auto_start(false)
	, auto_continue_on_reboot(false)
	, wait_for_complete_command(true)
{

}

void InstallConfiguration::Load(TiXmlElement * node)
{
	CHECK_BOOL(node != NULL,
		L"Expected 'configuration' node");

	CHECK_BOOL(0 == strcmp(node->Value(), "configuration"),
		L"Expected 'configuration' node, got '" << DVLib::string2wstring(node->Value()) << L"'");

	Configuration::Load(node);

    // defines where to extract files and auto-delete options
    cab_path = XML_ATTRIBUTE(node->Attribute("cab_path"));
	InstallerSession::Instance->SessionCABPath = cab_path;
    cab_path_autodelete = DVLib::wstring2bool(DVLib::UTF8string2wstring(node->Attribute("cab_path_autodelete")), true);
    // positions within the dialog
    dialog_position.FromString(DVLib::UTF8string2wstring(node->Attribute("dialog_position")));
    dialog_components_list_position.FromString(DVLib::UTF8string2wstring(node->Attribute("dialog_components_list_position")));
    dialog_message_position.FromString(DVLib::UTF8string2wstring(node->Attribute("dialog_message_position")));
    dialog_bitmap_position.FromString(DVLib::UTF8string2wstring(node->Attribute("dialog_bitmap_position")));
    dialog_otherinfo_link_position.FromString(DVLib::UTF8string2wstring(node->Attribute("dialog_otherinfo_link_position")));
    dialog_osinfo_position.FromString(DVLib::UTF8string2wstring(node->Attribute("dialog_osinfo_position")));
    dialog_install_button_position.FromString(DVLib::UTF8string2wstring(node->Attribute("dialog_install_button_position")));
    dialog_cancel_button_position.FromString(DVLib::UTF8string2wstring(node->Attribute("dialog_cancel_button_position")));
    dialog_skip_button_position.FromString(DVLib::UTF8string2wstring(node->Attribute("dialog_skip_button_position")));
    // other dialog options
	cancel_caption = XML_ATTRIBUTE(node->Attribute("cancel_caption"));
	dialog_bitmap = XML_ATTRIBUTE(node->Attribute("dialog_bitmap"));
	dialog_caption = XML_ATTRIBUTE(node->Attribute("dialog_caption"));
	dialog_message = XML_ATTRIBUTE(node->Attribute("dialog_message"));
	skip_caption = XML_ATTRIBUTE(node->Attribute("skip_caption"));
	install_caption = XML_ATTRIBUTE(node->Attribute("install_caption"));
	status_installed = XML_ATTRIBUTE(node->Attribute("status_installed"));
	status_notinstalled = XML_ATTRIBUTE(node->Attribute("status_notinstalled"));
	failed_exec_command_continue = XML_ATTRIBUTE(node->Attribute("failed_exec_command_continue"));
	installation_completed = XML_ATTRIBUTE(node->Attribute("installation_completed"));
	installation_none = XML_ATTRIBUTE(node->Attribute("installation_none"));
	reboot_required = XML_ATTRIBUTE(node->Attribute("reboot_required"));
    must_reboot_required = DVLib::wstring2bool(DVLib::UTF8string2wstring(node->Attribute("must_reboot_required")), false);
	installing_component_wait = XML_ATTRIBUTE(node->Attribute("installing_component_wait"));
	dialog_otherinfo_caption = XML_ATTRIBUTE(node->Attribute("dialog_otherinfo_caption"));
	dialog_otherinfo_link = XML_ATTRIBUTE(node->Attribute("dialog_otherinfo_link"));
	// completion commands
	complete_command = XML_ATTRIBUTE(node->Attribute("complete_command"));
	complete_command_basic = XML_ATTRIBUTE(node->Attribute("complete_command_basic"));
	complete_command_silent = XML_ATTRIBUTE(node->Attribute("complete_command_silent"));
	wait_for_complete_command = DVLib::wstring2bool(DVLib::UTF8string2wstring(node->Attribute("wait_for_complete_command")), true);
	auto_close_if_installed = DVLib::wstring2bool(DVLib::UTF8string2wstring(node->Attribute("auto_close_if_installed")), true);
    auto_close_on_error = DVLib::wstring2bool(DVLib::UTF8string2wstring(node->Attribute("auto_close_on_error")), false);
    allow_continue_on_error = DVLib::wstring2bool(DVLib::UTF8string2wstring(node->Attribute("allow_continue_on_error")), true);
    dialog_show_installed = DVLib::wstring2bool(DVLib::UTF8string2wstring(node->Attribute("dialog_show_installed")), true);
    dialog_show_required = DVLib::wstring2bool(DVLib::UTF8string2wstring(node->Attribute("dialog_show_required")), true);
    // message and caption to show during CAB extraction
    cab_dialog_caption = XML_ATTRIBUTE(node->Attribute("cab_dialog_caption"));
    cab_dialog_message = XML_ATTRIBUTE(node->Attribute("cab_dialog_message"));
    cab_cancelled_message = XML_ATTRIBUTE(node->Attribute("cab_cancelled_message"));
	// auto start
	auto_start = DVLib::wstring2bool(DVLib::UTF8string2wstring(node->Attribute("auto_start")), false);
	// auto start on reboot
	auto_continue_on_reboot = DVLib::wstring2bool(DVLib::UTF8string2wstring(node->Attribute("auto_continue_on_reboot")), false);
	// additional reboot cmd
    reboot_cmd = XML_ATTRIBUTE(node->Attribute("reboot_cmd"));
	// components
	TiXmlNode * child = NULL;
	while ( (child = node->IterateChildren("component", child)) != NULL)
	{
		TiXmlElement * node_component = child->ToElement();
		if (node_component == NULL)
			continue;

		std::wstring component_type = DVLib::UTF8string2wstring(node_component->Attribute("type"));

		shared_any<Component *, close_delete> component;
		if (component_type == L"msi") component = shared_any<Component *, close_delete>(new MsiComponent());
		else if (component_type == L"msu") component = shared_any<Component *, close_delete>(new MsuComponent());
		else if (component_type == L"cmd") component = shared_any<Component *, close_delete>(new CmdComponent());
		else if (component_type == L"openfile") component = shared_any<Component *, close_delete>(new OpenFileComponent());
		else 
		{
			THROW_EX(L"Unsupported component type: " << component_type);
		}

		component->Load(node_component);
		components.add(component);
	}

	LOG(L"Loaded " << components.size() << L" component(s) from configuration type=" << type 
		<< L" (lcid_filter=" << lcid_filter
		<< L", os_filter_greater=" << os_filter_greater
		<< L", os_filter_smaller=" << os_filter_smaller
		<< L", processor_architecture_filter=" << processor_architecture_filter
		<< L")");
}

Components InstallConfiguration::GetSupportedComponents(DVLib::LcidType lcidtype) const
{
	return components.GetSupportedComponents(lcidtype);
}

std::wstring InstallConfiguration::GetString(int indent) const
{
	std::wstringstream ss;
	for(int i = 0; i < indent; i++) ss << L" ";
	ss << L"Install" << Configuration::GetString() << std::endl;
	ss << components.GetString(indent + 1) << std::endl;
	return ss.str();
}