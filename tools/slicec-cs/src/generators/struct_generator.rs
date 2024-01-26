// Copyright (c) ZeroC, Inc.

use crate::builders::{
    AttributeBuilder, Builder, CommentBuilder, ContainerBuilder, FunctionBuilder, FunctionType,
};
use crate::cs_attributes::CsReadonly;
use crate::decoding::*;
use crate::encoding::*;
use crate::member_util::*;
use crate::slicec_ext::{CommentExt, EntityExt, MemberExt, TypeRefExt};
use slicec::code_block::CodeBlock;
use slicec::grammar::*;
use slicec::supported_encodings::SupportedEncodings;

pub fn generate_struct(struct_def: &Struct) -> CodeBlock {
    let escaped_identifier = struct_def.escape_identifier();
    let fields = struct_def.fields();
    let namespace = struct_def.namespace();

    let mut declaration = vec![struct_def.access_modifier()];
    if struct_def.has_attribute::<CsReadonly>() {
        declaration.push("readonly");
    }
    declaration.extend(["partial", "record", "struct"]);

    let mut builder = ContainerBuilder::new(&declaration.join(" "), &escaped_identifier);
    if let Some(summary) = struct_def.formatted_doc_comment_summary() {
        builder.add_comment("summary", summary);
    }
    builder
        .add_generated_remark("record struct", struct_def)
        .add_comments(struct_def.formatted_doc_comment_seealso())
        .add_obsolete_attribute(struct_def);

    builder.add_block(
        fields
            .iter()
            .map(|m| field_declaration(m))
            .collect::<Vec<_>>()
            .join("\n\n")
            .into(),
    );

    let mut main_constructor = FunctionBuilder::new(
        struct_def.access_modifier(),
        "",
        &escaped_identifier,
        FunctionType::BlockBody,
    );
    main_constructor.add_comment(
        "summary",
        format!(r#"Constructs a new instance of <see cref="{escaped_identifier}" />."#),
    );

    for field in &fields {
        main_constructor.add_parameter(
            &field.data_type().field_type_string(&namespace, false),
            field.parameter_name().as_str(),
            None,
            field.formatted_doc_comment_summary(),
        );
    }
    main_constructor.set_body({
        let mut code = CodeBlock::default();
        for field in &fields {
            writeln!(code, "this.{} = {};", field.field_name(), field.parameter_name(),);
        }
        code
    });
    builder.add_block(main_constructor.build());

    // Decode constructor
    let mut decode_body = generate_encoding_blocks(&fields, struct_def.supported_encodings(), false);

    if !struct_def.is_compact {
        writeln!(decode_body, "decoder.SkipTagged();");
    }
    builder.add_block(
            FunctionBuilder::new(
                struct_def.access_modifier(),
                "",
                &escaped_identifier,
                FunctionType::BlockBody,
            )
            .add_comment(
                "summary",
                format!(r#"Constructs a new instance of <see cref="{escaped_identifier}" /> and decodes its fields from a Slice decoder."#),
            )
            .add_parameter(
                "ref SliceDecoder",
                "decoder",
                None,
                Some("The Slice decoder.".to_owned()),
            )
            .set_body(decode_body)
            .build(),
        );

    // Encode method
    let mut encode_body = generate_encoding_blocks(&fields, struct_def.supported_encodings(), true);

    if !struct_def.is_compact {
        writeln!(encode_body, "encoder.EncodeVarInt32(Slice2Definitions.TagEndMarker);");
    }
    builder.add_block(
        FunctionBuilder::new(
            &(struct_def.access_modifier().to_owned() + " readonly"),
            "void",
            "Encode",
            FunctionType::BlockBody,
        )
        .add_comment("summary", "Encodes the fields of this struct with a Slice encoder.")
        .add_parameter(
            "ref SliceEncoder",
            "encoder",
            None,
            Some("The Slice encoder.".to_owned()),
        )
        .set_body(encode_body)
        .build(),
    );

    builder.build()
}

fn generate_encoding_blocks(fields: &[&Field], supported_encodings: SupportedEncodings, is_encode: bool) -> CodeBlock{
    let block_source_fn = match is_encode {
        true => encode_fields,
        false => decode_fields,
    };

    match supported_encodings[..] {
        [] => unreachable!("No supported encodings"),
        [encoding] => block_source_fn(fields, encoding),
        _ => {
            let mut slice1_block = block_source_fn(fields, Encoding::Slice1);
            let mut slice2_block = block_source_fn(fields, Encoding::Slice2);

            // Only write one encoding block if `slice1_block` and `slice2_block` are the same.
            if slice1_block.to_string() == slice2_block.to_string() {
                return slice2_block;
            }

            let encoding_variable = match is_encode {
                true => "encoder.Encoding",
                false => "decoder.Encoding",
            };

            if slice1_block.is_empty() && !slice2_block.is_empty() {
                format!(
                    "\
if ({encoding_variable} != SliceEncoding.Slice1) // Slice2 only
{{
{slice2_block}
}}
",
                    slice2_block = slice2_block.indent(),
                )
                .into()
            } else if !slice1_block.is_empty() && !slice2_block.is_empty() {
                format!(
                    "\
if ({encoding_variable} == SliceEncoding.Slice1)
{{
{slice1_block}
}}
else // Slice2
{{
{slice2_block}
}}
",
                    slice1_block = slice1_block.indent(),
                    slice2_block = slice2_block.indent(),
                )
                .into()
            } else {
                unreachable!("it is not possible to have an empty Slice2 encoding block with a non empty Slice1 encoding block");
            }
        }
    }
}
